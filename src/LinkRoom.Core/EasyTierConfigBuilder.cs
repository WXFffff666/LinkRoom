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

        // Single source of truth for IPv6 TOML keys (PathSelectionStrategy no longer emits them):
        // advanced options plus runtime IPv6-only detection, each written exactly once.
        if (advanced.PreferIPv6 || advanced.Ipv6Only || snapshot is { HasIPv6: true, HasIPv4: false })
            sb.AppendLine("enable_ipv6 = true");

        if (advanced.Ipv6Only)
            sb.AppendLine("disable_ipv4 = true");

        // Path flags consolidated in TOML only (no CLI duplication).
        // Must stay inside [flags] scope: any table header below (e.g. [proxy]) would capture them.
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

        if (advanced.EnableSocks5 && advanced.Socks5Port is >= 1024 and <= 65535)
        {
            sb.AppendLine();
            sb.AppendLine("[proxy]");
            sb.AppendLine($"socks5_port = {advanced.Socks5Port}");
        }

        if (advanced.EnableSecureMode)
        {
            // E2EE per-peer encryption (Noise handshake). easytier-core 2.6.4
            // does NOT auto-generate the keypair when loading a config file —
            // only the CLI --secure-mode path does (see SPIKE-SECUREMODE.md) —
            // so both local_private_key and local_public_key must be written.
            // The keypair is persisted per install (LinkRoomData/config/securemode.key)
            // so the identity stays stable across restarts. All nodes in a
            // network must enable secure mode consistently.
            var (priv, pub) = SecureModeKeys.LoadOrCreate();
            sb.AppendLine();
            sb.AppendLine("[secure_mode]");
            sb.AppendLine("enabled = true");
            sb.AppendLine($"local_private_key = \"{priv}\"");
            sb.AppendLine($"local_public_key = \"{pub}\"");
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
                    // Optional shared-node identity pin: with secure mode the
                    // Noise handshake verifies the relay's static public key;
                    // mismatch = connection rejected. Empty = encrypted but
                    // not identity-verified (official public node publishes
                    // no fixed key). Pin makes sense only with secure mode on.
                    if (!string.IsNullOrWhiteSpace(advanced.SharedNodePublicKey))
                        sb.AppendLine($"peer_public_key = \"{EscapeToml(advanced.SharedNodePublicKey.Trim())}\"");
                }
            }
        }

        // Server-side room lock via EasyTier ACL (zero-trust group filter).
        // Spiked: easytier-core parses [acl.acl_v1] in TOML identically to YAML
        // and logs "ACL rules built: N inbound" + "ACL rules hot reloaded" on
        // startup — see src/LinkRoom.Core/SPIKE-ACL.md.
        // Group "room-owner" is the only allowed source/dest; default_action=2
        // denies everything else. The group_secret MUST match across all nodes;
        // the host embeds it in the linkroom:// link (LinkCodeService.Encode) so
        // guests pick it up automatically.
        if (advanced.RoomLocked && !string.IsNullOrEmpty(room.AclSecret))
        {
            sb.AppendLine();
            sb.AppendLine("# --- server-side room lock (EasyTier ACL) ---");
            sb.AppendLine("[acl.acl_v1.group]");
            sb.AppendLine("members = [\"room-owner\"]");
            sb.AppendLine();
            sb.AppendLine("[[acl.acl_v1.group.declares]]");
            sb.AppendLine("group_name = \"room-owner\"");
            sb.AppendLine($"group_secret = \"{EscapeToml(room.AclSecret)}\"");
            sb.AppendLine();
            sb.AppendLine("[[acl.acl_v1.group.declares]]");
            sb.AppendLine("group_name = \"guest\"");
            sb.AppendLine($"group_secret = \"{EscapeToml(room.AclSecret)}\"");
            sb.AppendLine();
            sb.AppendLine("[[acl.acl_v1.chains]]");
            sb.AppendLine("name = \"room_lock_inbound\"");
            sb.AppendLine("chain_type = 1");
            sb.AppendLine("description = \"deny non-room-owner peers\"");
            sb.AppendLine("enabled = true");
            sb.AppendLine("default_action = 2");
            sb.AppendLine();
            sb.AppendLine("[[acl.acl_v1.chains.rules]]");
            sb.AppendLine("name = \"allow_room_owner\"");
            sb.AppendLine("description = \"allow room-owner to room-owner\"");
            sb.AppendLine("priority = 1000");
            sb.AppendLine("action = 1");
            sb.AppendLine("source_groups = [\"room-owner\"]");
            sb.AppendLine("destination_groups = [\"room-owner\"]");
            sb.AppendLine("protocol = 5");
            sb.AppendLine("enabled = true");
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
