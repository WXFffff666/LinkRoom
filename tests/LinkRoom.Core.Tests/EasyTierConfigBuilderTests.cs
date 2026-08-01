using LinkRoom.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinkRoom.Core.Tests;

public class EasyTierConfigBuilderTests
{
    readonly EasyTierConfigBuilder _builder = new(NullLogger<EasyTierConfigBuilder>.Instance);
    readonly PathSelectionStrategy _strategy = new(NullLogger<PathSelectionStrategy>.Instance);

    public EasyTierConfigBuilderTests()
    {
        // Deterministic data location for the temp config files written by BuildAsync.
        AppPaths.Configure(portableMode: true);
    }

    async Task<string> BuildTomlAsync(
        AdvancedOptions advanced,
        NetworkSnapshot? snapshot = null,
        PathRecommendation? path = null)
    {
        var cfg = await _builder.BuildAsync(new RoomOptions { RoomId = "ABC12345" }, snapshot, advanced, path);
        try
        {
            return await File.ReadAllTextAsync(cfg.ConfigFilePath);
        }
        finally
        {
            cfg.Cleanup();
        }
    }

    /// Maps each TOML table header ("" for root) to the keys defined in its scope.
    static Dictionary<string, HashSet<string>> ParseTables(string toml)
    {
        var tables = new Dictionary<string, HashSet<string>>();
        var current = "";
        foreach (var rawLine in toml.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith('['))
            {
                current = line.Trim('[', ']');
                tables.TryAdd(current, []);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            tables.TryAdd(current, []);
            tables[current].Add(line[..eq].Trim());
        }
        return tables;
    }

    static int Occurrences(Dictionary<string, HashSet<string>> tables, string key) =>
        tables.Values.Count(set => set.Contains(key));

    static NetworkSnapshot Ipv6OnlySnapshot() => new()
    {
        NatType = NatType.FullCone,
        StunReachable = true,
        PublicIPv6 = "2001:db8::1",
        PublicIPv4 = null,
    };

    [Fact]
    public async Task Ipv6Only_WritesIpv6KeysExactlyOnceInFlagsTable()
    {
        // IPv6-only network snapshot + forced Ipv6Only: both sources used to emit
        // enable_ipv6, which produced duplicate TOML keys before the fix.
        var advanced = new AdvancedOptions { Ipv6Only = true };
        var path = _strategy.Evaluate(Ipv6OnlySnapshot(), advanced);

        var tables = ParseTables(await BuildTomlAsync(advanced, Ipv6OnlySnapshot(), path));

        Assert.Equal(1, Occurrences(tables, "enable_ipv6"));
        Assert.Equal(1, Occurrences(tables, "disable_ipv4"));
        Assert.Contains("enable_ipv6", tables["flags"]);
        Assert.Contains("disable_ipv4", tables["flags"]);
        // Strategy suffix behavior preserved even though TOML keys moved out.
        Assert.Contains("+ipv6", path.Strategy);
        Assert.Contains("+ipv6-only", path.Strategy);
    }

    [Fact]
    public async Task Socks5WithPathFlags_PathFlagsStayInFlagsTable_NotInProxy()
    {
        var advanced = new AdvancedOptions { Ipv6Only = true, EnableSocks5 = true, Socks5Port = 1080 };
        var path = new PathRecommendation { TomlFlags = ["disable_tcp_hole_punching = true"] };

        var tables = ParseTables(await BuildTomlAsync(advanced, path: path));

        Assert.Contains("socks5_port", tables["proxy"]);
        Assert.Contains("disable_tcp_hole_punching", tables["flags"]);
        Assert.DoesNotContain("disable_tcp_hole_punching", tables["proxy"]);
        Assert.Equal(1, Occurrences(tables, "enable_ipv6"));
        Assert.Equal(1, Occurrences(tables, "disable_ipv4"));
    }

    [Fact]
    public async Task Socks5WithSnapshot_DisableUpnpInFlagsTable_NotInProxy()
    {
        var advanced = new AdvancedOptions { EnableSocks5 = true, Socks5Port = 1080 };
        var snapshot = new NetworkSnapshot
        {
            NatType = NatType.FullCone,
            StunReachable = true,
            PublicIPv4 = "203.0.113.10",
        };

        var tables = ParseTables(await BuildTomlAsync(advanced, snapshot));

        Assert.Contains("socks5_port", tables["proxy"]);
        Assert.Contains("disable_upnp", tables["flags"]);
        Assert.DoesNotContain("disable_upnp", tables["proxy"]);
    }

    [Fact]
    public async Task Default_NoIpv6_NoDisableIpv4OrEnableIpv6()
    {
        var tables = ParseTables(await BuildTomlAsync(new AdvancedOptions()));

        Assert.Equal(0, Occurrences(tables, "disable_ipv4"));
        Assert.Equal(0, Occurrences(tables, "enable_ipv6"));
        Assert.Contains("dhcp", tables["flags"]);
        Assert.Contains("listeners", tables["flags"]);
    }

    [Fact]
    public async Task PreferIpv6_WritesEnableIpv6Once_NoDisableIpv4()
    {
        var tables = ParseTables(await BuildTomlAsync(new AdvancedOptions { PreferIPv6 = true }));

        Assert.Equal(1, Occurrences(tables, "enable_ipv6"));
        Assert.Equal(0, Occurrences(tables, "disable_ipv4"));
        Assert.Contains("enable_ipv6", tables["flags"]);
    }

    [Fact]
    public async Task RuntimeIpv6OnlyNetwork_WritesEnableIpv6OnceFromSnapshot()
    {
        // Runtime detection branch relocated from PathSelectionStrategy into the builder.
        var path = _strategy.Evaluate(Ipv6OnlySnapshot(), new AdvancedOptions());

        var tables = ParseTables(await BuildTomlAsync(new AdvancedOptions(), Ipv6OnlySnapshot(), path));

        Assert.Equal(1, Occurrences(tables, "enable_ipv6"));
        Assert.Equal(0, Occurrences(tables, "disable_ipv4"));
        Assert.Contains("enable_ipv6", tables["flags"]);
    }

    [Fact]
    public async Task RoomLocked_ConfigContainsAclSection()
    {
        // Server-side room lock: when RoomLocked is true and the caller supplies
        // an AclSecret, the TOML must include the [acl.acl_v1] sections so
        // easytier-core's default-deny inbound chain drops non-room-owner peers.
        // Spiked: easytier-core parses this TOML and logs "ACL rules built: 1
        // inbound" + "peers::acl_filter hot reloaded" (see SPIKE-ACL.md).
        const string secret = "spike-group-secret-XYZ";
        var advanced = new AdvancedOptions { RoomLocked = true };
        var toml = await BuildTomlForRoomAsync(
            new RoomOptions { RoomId = "ABC12345", AclSecret = secret }, advanced);

        // EasyTier parses [acl.acl_v1] tables identically in TOML and YAML; the
        // following structural assertions are enough to confirm the builder
        // emitted a parseable ACL block.
        Assert.Contains("[acl.acl_v1.group]", toml);
        Assert.Contains("members = [\"room-owner\"]", toml);
        Assert.Contains("[[acl.acl_v1.group.declares]]", toml);
        Assert.Contains("group_name = \"room-owner\"", toml);
        Assert.Contains($"group_secret = \"{secret}\"", toml);
        Assert.Contains("group_name = \"guest\"", toml);
        Assert.Contains("[[acl.acl_v1.chains]]", toml);
        Assert.Contains("default_action = 2", toml); // 2 = deny (the lock)
        Assert.Contains("[[acl.acl_v1.chains.rules]]", toml);
        Assert.Contains("action = 1", toml); // 1 = allow for room-owner
        Assert.Contains("source_groups = [\"room-owner\"]", toml);
        Assert.Contains("destination_groups = [\"room-owner\"]", toml);
    }

    [Fact]
    public async Task RoomLocked_WithoutAclSecret_OmitsAclSection()
    {
        // Caller forgot to generate/forward a secret: builder must NOT emit a
        // half-configured ACL block (that would break easytier-core startup).
        // The client-side RoomLocked gate in MainViewModel still blocks this
        // scenario from reaching easytier-core, but the builder itself stays
        // fail-safe: no secret, no ACL.
        var advanced = new AdvancedOptions { RoomLocked = true };
        var toml = await BuildTomlForRoomAsync(
            new RoomOptions { RoomId = "ABC12345" }, advanced);

        Assert.DoesNotContain("[acl.acl_v1", toml);
        Assert.DoesNotContain("group_secret", toml);
    }

    [Fact]
    public async Task RoomUnlocked_NoAclSection()
    {
        var advanced = new AdvancedOptions { RoomLocked = false };
        var toml = await BuildTomlForRoomAsync(
            new RoomOptions { RoomId = "ABC12345", AclSecret = "ignored" }, advanced);

        Assert.DoesNotContain("[acl.acl_v1", toml);
    }

    [Fact]
    public async Task EnableSecureMode_True_WritesSecureModeSection()
    {
        // Spiked: easytier-core 2.6.4 parses [secure_mode] in TOML, but the
        // config-file path does NOT auto-generate the keypair — both
        // local_private_key and local_public_key must be present or every
        // outbound peer connection fails ("local private key is not set").
        // See src/LinkRoom.Core/SPIKE-SECUREMODE.md.
        var advanced = new AdvancedOptions { EnableSecureMode = true };
        var toml = await BuildTomlForRoomAsync(
            new RoomOptions { RoomId = "ABC12345" }, advanced);

        Assert.Contains("[secure_mode]", toml);
        Assert.Contains("enabled = true", toml);
        Assert.Contains("local_private_key = \"", toml);
        Assert.Contains("local_public_key = \"", toml);
    }

    [Fact]
    public async Task EnableSecureMode_False_OmitsSecureModeSection()
    {
        var advanced = new AdvancedOptions { EnableSecureMode = false };
        var toml = await BuildTomlForRoomAsync(
            new RoomOptions { RoomId = "ABC12345" }, advanced);

        Assert.DoesNotContain("[secure_mode]", toml);
        Assert.DoesNotContain("local_private_key", toml);
    }

    [Fact]
    public async Task EnableSecureMode_Default_IsTrue()
    {
        // LinkRoom defaults to secure mode on; existing users keep it after
        // upgrade (JSON deserialization leaves absent fields at their
        // initializer value).
        var toml = await BuildTomlForRoomAsync(
            new RoomOptions { RoomId = "ABC12345" }, new AdvancedOptions());

        Assert.Contains("[secure_mode]", toml);
        Assert.Contains("enabled = true", toml);
    }

    [Fact]
    public async Task SharedNodePublicKey_WritesPeerPublicKey()
    {
        // Pin the shared relay's X25519 public key: the Noise handshake then
        // rejects any node that does not present exactly this key.
        const string pub = "7XgILAJNb7tF5z+9kZZ967XyfauVMhSAZYydu3Ilfx8=";
        var advanced = new AdvancedOptions
        {
            EnableSecureMode = true,
            IsSharedNodeEnabled = true,
            SharedNodeUrls = "tcp://relay.example.com:11010",
            SharedNodePublicKey = pub,
        };
        var toml = await BuildTomlForRoomAsync(
            new RoomOptions { RoomId = "ABC12345" }, advanced);

        Assert.Contains("[[peer]]", toml);
        Assert.Contains($"peer_public_key = \"{pub}\"", toml);
        Assert.Contains("uri = \"tcp://relay.example.com:11010\"", toml);
    }

    [Fact]
    public async Task SharedNodePublicKey_Empty_OmitsPeerPublicKey()
    {
        // Empty = encrypted but not identity-verified (the official
        // public.easytier.top node publishes no fixed key).
        var advanced = new AdvancedOptions
        {
            EnableSecureMode = true,
            IsSharedNodeEnabled = true,
            SharedNodeUrls = "tcp://public.easytier.top:11010",
        };
        var toml = await BuildTomlForRoomAsync(
            new RoomOptions { RoomId = "ABC12345" }, advanced);

        Assert.Contains("[[peer]]", toml);
        Assert.DoesNotContain("peer_public_key", toml);
    }

    [Fact]
    public async Task SecureMode_KeypairIsStableAndValid()
    {
        // The persisted keypair must be a real 32-byte X25519 pair (base64)
        // whose public half derives from the private half; and it must stay
        // stable across calls (same identity across restarts).
        SecureModeKeys.ResetCache();
        var (priv1, pub1) = SecureModeKeys.LoadOrCreate();
        var (priv2, pub2) = SecureModeKeys.LoadOrCreate();

        Assert.Equal(priv1, priv2);
        Assert.Equal(pub1, pub2);
        Assert.Equal(44, priv1.Length); // 32 bytes base64
        Assert.Equal(44, pub1.Length);
        Assert.NotEqual(priv1, pub1);
        Assert.Equal(
            Convert.ToBase64String(SecureModeKeys.DerivePublicKey(Convert.FromBase64String(priv1))),
            pub1);
    }

    async Task<string> BuildTomlForRoomAsync(RoomOptions room, AdvancedOptions advanced)
    {
        var cfg = await _builder.BuildAsync(room, null, advanced);
        try
        {
            return await File.ReadAllTextAsync(cfg.ConfigFilePath);
        }
        finally
        {
            cfg.Cleanup();
        }
    }
}
