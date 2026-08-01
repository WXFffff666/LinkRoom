using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinkRoom.Core;

/// <summary>
/// Lightweight in-room chat over the EasyTier virtual network (plain TCP).
///
/// Host mode: binds a TcpListener to the node's virtual interface IP
/// (NodeInfo.IPv4) — NEVER 0.0.0.0, so the service is not exposed to the
/// host LAN. Client mode: connects to the host's virtual IP.
///
/// Security: NO E2E encryption here — traffic rides the EasyTier tunnel,
/// which is already encrypted in secure mode. Do not add a second layer.
/// Messages are in-memory only (no persistence) and are NEVER written to
/// the log — chat content is excluded from LinkRoom's logging by design.
/// </summary>
public sealed class ChatService
{
    /// <summary>Fixed port for the in-room chat service (configurable constant).</summary>
    public const int DefaultPort = 15889;

    /// <summary>Max messages kept in <see cref="Messages"/> (newest 100).</summary>
    public const int MaxMessages = 100;

    private readonly ILogger<ChatService> _logger;
    private readonly object _sync = new();
    private readonly List<ChatMessage> _messages = new();
    private readonly HashSet<TcpClient> _clients = new();

    private TcpListener? _listener;
    private TcpClient? _client;          // client-mode connection to the host
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private Task? _receiveLoop;
    private int _port = DefaultPort;

    /// <summary>Raised (on a background thread) for every accepted message — UI must marshal.</summary>
    public event Action<ChatMessage>? MessageReceived;

    /// <summary>Raised when a client is dropped for sending a malformed frame.</summary>
    public event Action<string>? ClientRejected;

    public ChatService(ILogger<ChatService>? logger = null)
    {
        _logger = logger ?? NullLogger<ChatService>.Instance;
    }

    /// <summary>
    /// Max wait for a frame body after its length header was read. Guards against
    /// half-frames (declared length never fulfilled) hanging the receive loop —
    /// such frames are treated as protocol violations and the client is dropped.
    /// </summary>
    internal TimeSpan BodyReadTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public bool IsRunning
    {
        get { lock (_sync) return _listener != null || _client != null; }
    }

    /// <summary>Snapshot of the last <see cref="MaxMessages"/> messages.</summary>
    public IReadOnlyList<ChatMessage> Messages
    {
        get { lock (_sync) return _messages.ToArray(); }
    }

    /// <summary>
    /// Starts the chat session. Host binds <paramref name="address"/> (the node's
    /// virtual IP); client connects to <paramref name="address"/> (the host's virtual IP).
    /// Restart-safe: a previous session is stopped first.
    /// </summary>
    public async Task StartAsync(bool isHost, string address, int port = DefaultPort)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("聊天需要虚拟 IP 地址", nameof(address));

        await StopAsync();

        lock (_sync) _port = port;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        if (isHost) await StartHostAsync(address, port, token);
        else await StartClientAsync(address, port, token);
    }

    private Task StartHostAsync(string bindIp, int port, CancellationToken ct)
    {
        // Bind the virtual interface IP only — 0.0.0.0 would expose the
        // chat service to the host's physical LAN (Metis m3).
        var listener = new TcpListener(IPAddress.Parse(bindIp), port);
        listener.Start();
        lock (_sync) _listener = listener;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, ct), ct);
        _logger.LogInformation("聊天服务已监听 {Ip}:{Port}（虚拟网）", bindIp, port);
        return Task.CompletedTask;
    }

    private async Task StartClientAsync(string hostIp, int port, CancellationToken ct)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Parse(hostIp), port, ct);
        }
        catch
        {
            client.Dispose();
            throw;
        }
        lock (_sync) _client = client;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(client, ct), ct);
        _logger.LogInformation("聊天服务已连接房主 {Ip}:{Port}（虚拟网）", hostIp, port);
    }

    /// <summary>
    /// Sends a message. On success the message is appended locally (echo) and
    /// broadcast to peers. Returns null on success, or a user-facing error
    /// ("未连接房间", over-limit, no members...) — chat content never logs.
    /// </summary>
    public string? SendMessage(string from, string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return null;

        byte[] frame;
        try
        {
            frame = ChatProtocol.Encode(from, DateTimeOffset.Now, msg);
        }
        catch (ChatFrameException ex)
        {
            return ex.Message;
        }

        TcpClient? peer = null;
        TcpClient[]? peers = null;
        lock (_sync)
        {
            if (_listener != null)
            {
                if (_clients.Count == 0) return "房间内暂无其他成员";
                peers = _clients.ToArray();
            }
            else if (_client is { Connected: true })
            {
                peer = _client;
            }
            else
            {
                return "未连接房间";
            }
        }

        if (peers != null)
        {
            foreach (var c in peers)
            {
                try { c.GetStream().Write(frame, 0, frame.Length); }
                catch { DropClient(c); }
            }
        }
        else
        {
            try { peer!.GetStream().Write(frame, 0, frame.Length); }
            catch { return "未连接房间"; }
        }

        AppendMessage(new ChatMessage(from, DateTimeOffset.Now, msg));
        return null;
    }

    /// <summary>
    /// Stops the chat session: cancels loops, stops the listener, closes all sockets.
    /// </summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        TcpListener? listener;
        TcpClient? client;
        TcpClient[] clients;

        lock (_sync)
        {
            cts = _cts;
            _cts = null;
            listener = _listener;
            _listener = null;
            client = _client;
            _client = null;
            clients = _clients.ToArray();
            _clients.Clear();
        }

        if (cts != null) await cts.CancelAsync(); // VSTHRD103: prefer CancelAsync in async methods
        try { listener?.Stop(); } catch { }
        try { client?.Dispose(); } catch { }
        foreach (var c in clients)
        {
            try { c.Dispose(); } catch { }
        }

        var loops = new[] { _acceptLoop, _receiveLoop };
        _acceptLoop = null;
        _receiveLoop = null;
        foreach (var t in loops)
        {
            if (t != null) { try { await t; } catch { } }
        }

        cts?.Dispose();
        if (listener != null || client != null)
            _logger.LogInformation("聊天服务已停止");
    }

    /// <summary>Appends a message, trimming to the newest <see cref="MaxMessages"/>.</summary>
    internal void AppendMessage(ChatMessage message)
    {
        lock (_sync)
        {
            _messages.Add(message);
            while (_messages.Count > MaxMessages) _messages.RemoveAt(0);
        }
        MessageReceived?.Invoke(message);
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break;
                _logger.LogWarning("聊天连接接受失败: {Error}", ex.Message);
                continue;
            }

            lock (_sync) _clients.Add(client);
            _ = Task.Run(() => ReceiveLoopAsync(client, ct), ct);
        }
    }

    private async Task ReceiveLoopAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            var stream = client.GetStream();
            var header = new byte[ChatProtocol.HeaderBytes];

            while (!ct.IsCancellationRequested)
            {
                var got = await ReadExactlyAsync(stream, header, ct);
                if (got == 0) break;                        // clean EOF
                if (got < ChatProtocol.HeaderBytes) break;  // truncated header — protocol violation

                var len = header[0] | (header[1] << 8);
                if (len == 0 || len > ChatProtocol.MaxMessageBytes)
                {
                    Reject(client, "非法帧长度");
                    return;
                }

                var body = new byte[len];
                int gotBody;
                using (var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    // Half-frame guard: never wait forever for a body whose
                    // declared length is not fulfilled (malicious/broken client).
                    bodyCts.CancelAfter(BodyReadTimeout);
                    try
                    {
                        gotBody = await ReadExactlyAsync(stream, body, bodyCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        Reject(client, "帧长度与数据不符");
                        return;
                    }
                }
                if (gotBody < len)
                {
                    Reject(client, "帧长度与数据不符");
                    return;
                }

                if (!ChatProtocol.TryDecode(body, out var message) || message == null)
                {
                    Reject(client, "无法解析的聊天帧");
                    return;
                }

                AppendMessage(message);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* peer vanished — drop silently */ }
        finally
        {
            DropClient(client);
            try { client.Dispose(); } catch { }
        }
    }

    private void Reject(TcpClient client, string reason)
    {
        _logger.LogWarning("聊天客户端异常帧，断开连接: {Reason}", reason);
        ClientRejected?.Invoke(reason);
        DropClient(client);
    }

    private void DropClient(TcpClient client)
    {
        lock (_sync) _clients.Remove(client);
    }

    private static async Task<int> ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
