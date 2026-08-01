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
}
