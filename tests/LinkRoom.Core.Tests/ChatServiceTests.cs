using System.Net;
using System.Net.Sockets;

namespace LinkRoom.Core.Tests;

/// <summary>
/// In-room chat tests: ChatProtocol frame codec + ChatService lifecycle over
/// loopback (real TCP, no EasyTier needed). The chat wire format is
/// [2-byte little-endian length][UTF-8 JSON {from, ts, msg}] with a 4 KB cap.
/// </summary>
public class ChatServiceTests
{
    // ---------- ChatProtocol: frame codec ----------

    [Fact]
    public void EncodeDecode_RoundTrip_PreservesFromTsMsg()
    {
        var ts = new DateTimeOffset(2026, 8, 2, 12, 34, 56, TimeSpan.FromHours(8));
        var frame = ChatProtocol.Encode("Alice", ts, "大家好");

        Assert.Equal(ChatProtocol.HeaderBytes, 2);
        Assert.Equal(frame.Length, ChatProtocol.HeaderBytes + frame[0] + (frame[1] << 8));
        Assert.True(ChatProtocol.TryDecode(frame.AsSpan(ChatProtocol.HeaderBytes), out var msg));
        Assert.NotNull(msg);
        Assert.Equal("Alice", msg!.From);
        Assert.Equal(ts, msg.Ts);
        Assert.Equal("大家好", msg.Msg);
    }

    [Fact]
    public void Encode_Utf8Chinese_RoundTrips()
    {
        var frame = ChatProtocol.Encode("张三", DateTimeOffset.UtcNow, "中文消息 ✓");
        Assert.True(ChatProtocol.TryDecode(frame.AsSpan(ChatProtocol.HeaderBytes), out var msg));
        Assert.Equal("中文消息 ✓", msg!.Msg);
    }

    [Fact]
    public void Encode_MessageOver4Kb_ThrowsChatFrameException()
    {
        var big = new string('长', 2000); // 3 bytes/char in UTF-8 → 6000 bytes > 4096
        var ex = Assert.Throws<ChatFrameException>(() =>
            ChatProtocol.Encode("A", DateTimeOffset.UtcNow, big));
        Assert.Contains("4096", ex.Message);
    }

    [Fact]
    public void TryDecode_MalformedJson_ReturnsFalse()
    {
        var body = "{\"from\": \"A\", \"ts\": "u8.ToArray(); // truncated JSON
        Assert.False(ChatProtocol.TryDecode(body, out _));
    }

    [Fact]
    public void TryDecode_EmptyOrOversizedBody_ReturnsFalse()
    {
        Assert.False(ChatProtocol.TryDecode(ReadOnlySpan<byte>.Empty, out _));
        var oversized = new byte[ChatProtocol.MaxMessageBytes + 1];
        Assert.False(ChatProtocol.TryDecode(oversized, out _));
    }

    [Fact]
    public void TryDecode_MissingFields_ReturnsFalse()
    {
        var body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { from = "A" });
        Assert.False(ChatProtocol.TryDecode(body, out _));
    }

    // ---------- ChatService: message list trimming ----------

    [Fact]
    public void AppendMessage_TrimsListToNewest100()
    {
        var svc = new ChatService();
        for (var i = 0; i < 120; i++)
            svc.AppendMessage(new ChatMessage("tester", DateTimeOffset.UtcNow, $"msg {i}"));

        Assert.Equal(100, svc.Messages.Count);
        Assert.Equal("msg 20", svc.Messages[0].Msg);  // oldest kept = 120 - 100
        Assert.Equal("msg 119", svc.Messages[^1].Msg);
    }

    // ---------- ChatService: lifecycle over loopback ----------

    [Fact]
    public void SendMessage_WhenNotRunning_ReturnsNotConnected()
    {
        var svc = new ChatService();
        Assert.False(svc.IsRunning);
        Assert.Equal("未连接房间", svc.SendMessage("tester", "hello"));
    }

    [Fact]
    public async Task HostWithNoClients_Send_ReturnsNoMembers()
    {
        var svc = new ChatService();
        var port = GetFreePort();
        await svc.StartAsync(isHost: true, "127.0.0.1", port);
        try
        {
            Assert.True(svc.IsRunning);
            Assert.Equal("房间内暂无其他成员", svc.SendMessage("tester", "hello"));
        }
        finally
        {
            await svc.StopAsync();
        }
    }

    [Fact]
    public async Task ClientToHost_RoundTrip_DeliversMessage()
    {
        var svc = new ChatService();
        var port = GetFreePort();
        await svc.StartAsync(isHost: true, "127.0.0.1", port);

        var tcs = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.MessageReceived += m => tcs.TrySetResult(m);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var frame = ChatProtocol.Encode("guest", DateTimeOffset.UtcNow, "hello host");
        await client.GetStream().WriteAsync(frame);

        var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("guest", msg.From);
        Assert.Equal("hello host", msg.Msg);
        Assert.Single(svc.Messages);

        await svc.StopAsync();
        Assert.False(svc.IsRunning);
    }

    [Fact]
    public async Task HostBroadcast_ReachesClient()
    {
        var svc = new ChatService();
        var port = GetFreePort();
        await svc.StartAsync(isHost: true, "127.0.0.1", port);

        var tcs = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.MessageReceived += m => tcs.TrySetResult(m);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);

        // host -> broadcast -> guest
        // The host registers accepted clients asynchronously (AcceptLoopAsync), so
        // right after ConnectAsync the client may not be in _clients yet and
        // SendMessage would return "房间内暂无其他成员". Poll-retry until the
        // broadcast succeeds (host has accepted the guest) or give up after 1s.
        string? result = null;
        for (var i = 0; i < 20; i++)
        {
            result = svc.SendMessage("host", "hello guest");
            if (result == null) break;
            await Task.Delay(50);
        }
        Assert.Null(result);

        var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("host", msg.From);
        Assert.Equal("hello guest", msg.Msg);

        await svc.StopAsync();
    }

    [Fact]
    public async Task MalformedFrame_RejectsAndDisconnectsClient()
    {
        var svc = new ChatService();
        var rejected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.ClientRejected += r => rejected.TrySetResult(r);
        var port = GetFreePort();
        await svc.StartAsync(isHost: true, "127.0.0.1", port);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();

        // Frame header declares 100 bytes; body is 100 bytes of garbage (not JSON).
        var badBody = new byte[100];
        Array.Fill(badBody, (byte)'x');
        stream.Write([100, 0]);
        stream.Write(badBody);

        // Server must drop the client: read until EOF or exception.
        var buffer = new byte[64];
        var closed = false;
        try
        {
            using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (await stream.ReadAsync(buffer, readCts.Token) > 0) { }
            closed = true; // EOF → server closed the socket
        }
        catch { closed = true; }

        Assert.True(closed, "服务端应断开异常帧客户端");
        Assert.Equal("无法解析的聊天帧", await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        await svc.StopAsync();
    }

    [Fact]
    public async Task HalfFrame_DeclaredLengthNeverFulfilled_RejectsAndDisconnects()
    {
        // A frame header declaring 100 bytes with only 3 bytes following must not
        // hang the receive loop forever — the client is dropped after the body
        // read timeout (half-frame guard).
        var svc = new ChatService { BodyReadTimeout = TimeSpan.FromMilliseconds(200) };
        var rejected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.ClientRejected += r => rejected.TrySetResult(r);
        var port = GetFreePort();
        await svc.StartAsync(isHost: true, "127.0.0.1", port);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        stream.Write([100, 0, 1, 2, 3]);

        Assert.Equal("帧长度与数据不符", await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        await svc.StopAsync();
    }

    [Fact]
    public async Task Start_ThenRestart_SamePort_Succeeds()
    {
        // Reconnect path: StartAsync must stop the previous session first,
        // otherwise rebinding the same port would throw.
        var svc = new ChatService();
        var port = GetFreePort();
        await svc.StartAsync(isHost: true, "127.0.0.1", port);
        await svc.StartAsync(isHost: true, "127.0.0.1", port);
        try
        {
            Assert.True(svc.IsRunning);
        }
        finally
        {
            await svc.StopAsync();
        }
        Assert.False(svc.IsRunning);
    }

    static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
