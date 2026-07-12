using System.Text;
using Microsoft.Extensions.Logging;
using LinkRoom.Network;

namespace LinkRoom.Core;

public sealed class EasyTierConfigBuilder
{
    readonly ILogger<EasyTierConfigBuilder> _logger;

    public EasyTierConfigBuilder(ILogger<EasyTierConfigBuilder> logger) => _logger = logger;

    public async Task<EasyTierLaunchConfig> BuildAsync(
        RoomOptions room,
        NetworkSnapshot? snapshot,
        AdvancedOptions advanced,
        PathRecommendation? path = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(room.RoomId) || room.RoomId.Length is < 3 or > 64)
            throw new ArgumentException("Room ID must be 3-64 characters.");
        if (room.Password.Length > 128)
            throw new ArgumentException("Password max 128 characters.");

        var toml = BuildToml(room, snapshot, advanced, path);
        AppPaths.EnsureDataDirectories();
        var configPath = Path.Combine(AppPaths.TempDir, $"easytier-{Guid.NewGuid():N}.toml");
        await File.WriteAllTextAsync(configPath, toml, Encoding.UTF8, ct);

        var cliFlags = new List<string>();
        if (path != null)
            cliFlags.AddRange(path.Flags);

        _logger.LogDebug("Config written to {Path} ({Bytes} bytes)", configPath, toml.Length);
        return new EasyTierLaunchConfig(configPath, cliFlags);
    }

    static string BuildToml(RoomOptions room, NetworkSnapshot? snapshot, AdvancedOptions advanced, PathRecommendation? path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# LinkRoom auto-generated EasyTier config");
        sb.AppendLine("[network_identity]");
        sb.AppendLine($"network_name = \"{EscapeToml(room.RoomId)}\"");
        if (!string.IsNullOrEmpty(room.Password))
            sb.AppendLine($"network_secret = \"{EscapeToml(room.Password)}\"");
        sb.AppendLine();

        sb.AppendLine("[flags]");
        if (!string.IsNullOrWhiteSpace(advanced.StaticVirtualIp))
            sb.AppendLine($"ipv4 = \"{advanced.StaticVirtualIp}\"");
        else
            sb.AppendLine("dhcp = true");

        sb.AppendLine($"listeners = [\"tcp://0.0.0.0:{advanced.ListenerPort}\", \"udp://0.0.0.0:{advanced.ListenerPort}\"]");

        if (advanced.Mtu is >= 576 and <= 1500)
            sb.AppendLine($"mtu = {advanced.Mtu}");

        if (advanced.PreferIPv6 || advanced.Ipv6Only)
            sb.AppendLine("enable_ipv6 = true");

        if (advanced.Ipv6Only)
            sb.AppendLine("disable_ipv4 = true");

        if (advanced.EnableSocks5 && advanced.Socks5Port is >= 1024 and <= 65535)
        {
            sb.AppendLine();
            sb.AppendLine("[proxy]");
            sb.AppendLine($"socks5_port = {advanced.Socks5Port}");
        }

        // Path flags consolidated in TOML only (no CLI duplication)
        if (path != null)
        {
            foreach (var flag in path.TomlFlags)
                sb.AppendLine(flag);
        }

        if (snapshot != null)
        {
            if (snapshot.IsSymmetric && !advanced.IsUpnpDisabled)
                sb.AppendLine("disable_upnp = false");
            else
                sb.AppendLine("disable_upnp = true");
        }

        if (advanced.IsSharedNodeEnabled && !string.IsNullOrWhiteSpace(advanced.SharedNodeUrls))
        {
            sb.AppendLine();
            foreach (var url in advanced.SharedNodeUrls.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = url.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    sb.AppendLine("[[peer]]");
                    sb.AppendLine($"uri = \"{EscapeToml(trimmed)}\"");
                }
            }
        }

        return sb.ToString();
    }

    static string EscapeToml(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static string MapLogLevel(string? level) => level?.ToLowerInvariant() switch
    {
        "debug" => "debug",
        "warning" => "warn",
        "error" => "error",
        _ => "info",
    };
}

public record EasyTierLaunchConfig(string ConfigFilePath, IReadOnlyList<string> CliFlags)
{
    public string CliArguments
    {
        get
        {
            var sb = new StringBuilder($"--config-file \"{ConfigFilePath}\"");
            foreach (var f in CliFlags)
                sb.Append(' ').Append(f);
            return sb.ToString();
        }
    }

    public void Cleanup()
    {
        try { File.Delete(ConfigFilePath); } catch { }
    }
}
