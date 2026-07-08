using System.Text;
using Microsoft.Extensions.Logging;
using LinkRoom.Network;

namespace LinkRoom.Core;

/// <summary>
/// Builds an EasyTier TOML config file from room options and network snapshot.
/// The room password is written ONLY to the config file, NEVER to CLI args.
/// </summary>
public sealed class EasyTierConfigBuilder
{
    private readonly ILogger<EasyTierConfigBuilder> _logger;

    public EasyTierConfigBuilder(ILogger<EasyTierConfigBuilder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates a TOML config file and returns the launch arguments for easytier-core.
    /// The temp config file contains network identity, flags, and optional shared nodes.
    /// The password lives ONLY in the file, not on the command line.
    /// </summary>
    public async Task<EasyTierLaunchConfig> BuildAsync(
        RoomOptions room,
        NetworkSnapshot? snapshot,
        AdvancedOptions advanced,
        CancellationToken ct = default)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(room.RoomId) || room.RoomId.Length < 3 || room.RoomId.Length > 64)
            throw new ArgumentException("Room ID must be 3-64 characters.");
        if (string.IsNullOrEmpty(room.Password) || room.Password.Length > 128)
            throw new ArgumentException("Password must be non-empty and max 128 characters.");

        // Build TOML config file
        var toml = BuildToml(room, snapshot, advanced);

        var tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkRoom", "temp");
        Directory.CreateDirectory(tempDir);

        var configPath = Path.Combine(tempDir, $"easytier-{Guid.NewGuid():N}.toml");
        await File.WriteAllTextAsync(configPath, toml, Encoding.UTF8, ct);

        // Build CLI args — NEVER include --network-secret here
        var args = $"--config-file \"{configPath}\"";

        _logger.LogDebug("Config written to {Path} ({Bytes} bytes)", configPath, toml.Length);

        return new EasyTierLaunchConfig(configPath, args);
    }

    private static string BuildToml(RoomOptions room, NetworkSnapshot? snapshot, AdvancedOptions advanced)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# LinkRoom auto-generated EasyTier config");
        sb.AppendLine($"[network_identity]");
        sb.AppendLine($"network_name = \"{EscapeTomlString(room.RoomId)}\"");
        sb.AppendLine($"network_secret = \"{EscapeTomlString(room.Password)}\"");
        sb.AppendLine();

        sb.AppendLine("[flags]");

        // Virtual IP
        if (!string.IsNullOrWhiteSpace(advanced.StaticVirtualIp))
        {
            sb.AppendLine($"# Static virtual IP (from advanced settings)");
            sb.AppendLine($"ipv4 = \"{advanced.StaticVirtualIp}\"");
        }
        else
        {
            sb.AppendLine("dhcp = true");
        }

        // Listener port
        sb.AppendLine($"# P2P listener port");
        sb.AppendLine($"listeners = [\"tcp://0.0.0.0:{advanced.ListenerPort}\", \"udp://0.0.0.0:{advanced.ListenerPort}\"]");

        // NAT-specific flags from snapshot
        if (snapshot != null)
        {
            if (snapshot.IsSymmetric)
            {
                sb.AppendLine("# Symmetric NAT detected: enable TCP hole punching");
                sb.AppendLine("disable_tcp_hole_punching = false");
                sb.AppendLine("disable_udp_hole_punching = false");

                if (!advanced.IsUpnpDisabled)
                {
                    sb.AppendLine("# UPnP enabled (user opt-in)");
                    sb.AppendLine("disable_upnp = false");
                }
                else
                {
                    sb.AppendLine("disable_upnp = true");
                }
            }
            else if (snapshot.IsFullCone)
            {
                sb.AppendLine("# Full Cone NAT: prefer UDP hole punching");
                sb.AppendLine("disable_udp_hole_punching = false");
                sb.AppendLine("disable_upnp = true");
            }
            else
            {
                sb.AppendLine("# Unknown/restricted NAT: try both");
                sb.AppendLine("disable_tcp_hole_punching = false");
                sb.AppendLine("disable_udp_hole_punching = false");
                sb.AppendLine("disable_upnp = true");
            }

            if (snapshot.HasIPv6 && !snapshot.HasIPv4)
            {
                sb.AppendLine("# IPv6-only network: prefer IPv6");
                sb.AppendLine("enable_ipv6 = true");
            }
        }

        // Shared nodes
        if (advanced.IsSharedNodeEnabled && !string.IsNullOrWhiteSpace(advanced.SharedNodeUrls))
        {
            sb.AppendLine();
            sb.AppendLine("# Shared node peers (user-enabled)");
            foreach (var url in advanced.SharedNodeUrls.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = url.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    sb.AppendLine($"[[peer]]");
                    sb.AppendLine($"uri = \"{EscapeTomlString(trimmed)}\"");
                }
            }
        }

        return sb.ToString();
    }

    private static string EscapeTomlString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

/// <summary>
/// User-supplied room connection options.
/// </summary>
public record RoomOptions
{
    public string RoomId { get; init; } = "";
    public string Password { get; init; } = "";
}

/// <summary>
/// User-supplied advanced configuration options.
/// </summary>
public record AdvancedOptions
{
    public bool IsSharedNodeEnabled { get; init; }
    public string? SharedNodeUrls { get; init; }
    public string? LogLevel { get; init; } = "Info";
    public bool IsUpnpDisabled { get; init; } = true;
    public string? CustomStunServers { get; init; }
    public int MaxReconnectAttempts { get; init; } = 5;
    public string? StaticVirtualIp { get; init; }
    public int ListenerPort { get; init; } = 11010;
}

/// <summary>
/// Result of building an EasyTier launch configuration.
/// </summary>
public record EasyTierLaunchConfig(string ConfigFilePath, string CliArguments)
{
    /// <summary>
    /// Deletes the temporary config file. Call after easytier-core has read it.
    /// </summary>
    public void Cleanup()
    {
        try { File.Delete(ConfigFilePath); } catch { /* best effort */ }
    }
}
