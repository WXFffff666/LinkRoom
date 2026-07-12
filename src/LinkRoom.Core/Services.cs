using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

public sealed class PeerPingService
{
    readonly ILogger<PeerPingService> _logger;

    public PeerPingService(ILogger<PeerPingService> logger) => _logger = logger;

    public async Task<(bool Ok, long Ms)> PingAsync(string host, CancellationToken ct = default)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 3000);
            return (reply.Status == IPStatus.Success, reply.RoundtripTime);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ping failed for {Host}", host);
            return (false, -1);
        }
    }
}

public sealed class WebPanelService
{
    readonly string _webEmbedPath;

    public WebPanelService(string runtimeDir) =>
        _webEmbedPath = Path.Combine(runtimeDir, "easytier-web-embed.exe");

    public bool IsAvailable => File.Exists(_webEmbedPath);

    public Process? Launch(int port = 15888)
    {
        if (!IsAvailable) return null;
        return Process.Start(new ProcessStartInfo
        {
            FileName = _webEmbedPath,
            Arguments = $"--rpc-portal 127.0.0.1:{port}",
            UseShellExecute = true,
        });
    }

    public void OpenInBrowser(int port = 15888) =>
        Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}") { UseShellExecute = true });
}

public static class CliRunner
{
    public sealed record CliOptions(
        bool Create, bool Join, string? RoomId, string? Password,
        bool Headless, bool Minimized, bool LanMode, bool SharedNode);

    public static CliOptions? Parse(string[] args)
    {
        if (args.Length == 0) return null;
        bool create = false, join = false, headless = false, minimized = false, lan = false, shared = false;
        string? room = null, pass = null;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i].ToLowerInvariant();
            switch (a)
            {
                case "--create": create = true; break;
                case "--join": join = true; break;
                case "--headless": headless = true; break;
                case "--minimized": minimized = true; break;
                case "--lan-mode": lan = true; break;
                case "--shared-node": shared = true; break;
                case "--room":
                case "-r":
                    if (i + 1 < args.Length) room = args[++i];
                    break;
                case "--pass":
                case "-p":
                    if (i + 1 < args.Length) pass = args[++i];
                    break;
                default:
                    if (a.StartsWith("linkroom://", StringComparison.OrdinalIgnoreCase) || a.Contains(':'))
                    {
                        var decoded = LinkCodeService.Decode(args[i]);
                        room = decoded.RoomId;
                        pass ??= decoded.Password;
                        join = true;
                    }
                    else if (room == null && a.Length >= 3) room = args[i];
                    break;
            }
        }

        if (!create && !join && room != null) join = true;
        if (!create && !join) return minimized ? new CliOptions(false, false, null, null, false, true, lan, shared) : null;
        return new CliOptions(create, join, room, pass, headless, minimized, lan, shared);
    }
}

public static class AdminHelper
{
    public static bool IsAdministrator()
    {
        try
        {
            var w = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(w)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static bool RequestLanModeAdmin(bool useLanMode)
    {
        if (!useLanMode) return true;
        if (IsAdministrator()) return true;
        MessageBoxCompat.Show(
            "LAN 模式需要虚拟网卡（Wintun），请以管理员身份重新运行 LinkRoom。",
            "需要管理员权限");
        return false;
    }
}

/// <summary>Abstraction to avoid WPF reference in Core for admin messages.</summary>
public static class MessageBoxCompat
{
    public static Action<string, string>? ShowHandler { get; set; }
    public static void Show(string message, string title) => ShowHandler?.Invoke(message, title);
}
