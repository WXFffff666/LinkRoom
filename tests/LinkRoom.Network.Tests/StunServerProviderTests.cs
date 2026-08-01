using Microsoft.Extensions.Logging.Abstractions;

namespace LinkRoom.Network.Tests;

public class StunServerProviderTests
{
    static StunServerProvider NewProvider() => new(new NullLogger<StunServerProvider>());

    [Fact]
    public void Resolve_NoCustom_ReturnsAllDefaults()
    {
        var servers = NewProvider().Resolve(null).ToList();

        Assert.Equal(StunServerProvider.DefaultServers.Length, servers.Count);
        Assert.Contains(("stun.l.google.com", 19302), servers);
        Assert.Contains(("stun.syncthing.net", 3478), servers);
    }

    [Fact]
    public void Resolve_CustomCsv_AppendsToDefaults()
    {
        var servers = NewProvider().Resolve("stun.example.com:3478").ToList();

        Assert.Equal(StunServerProvider.DefaultServers.Length + 1, servers.Count);
        Assert.Contains(("stun.example.com", 3478), servers);
    }

    [Fact]
    public void Resolve_CustomCsv_MultipleEntries_AllIncluded()
    {
        var servers = NewProvider().Resolve("stun.a.com:1000,stun.b.com:2000").ToList();

        Assert.Equal(StunServerProvider.DefaultServers.Length + 2, servers.Count);
        Assert.Contains(("stun.a.com", 1000), servers);
        Assert.Contains(("stun.b.com", 2000), servers);
    }

    [Fact]
    public void Resolve_CustomCsv_SkipsInvalidEntries()
    {
        // 无冒号、端口非数字、带空白修饰的条目；只有最后一条有效
        var servers = NewProvider().Resolve("badentry,host:notaport,  stun.ok.com:9999  ").ToList();

        Assert.Equal(StunServerProvider.DefaultServers.Length + 1, servers.Count);
        Assert.Contains(("stun.ok.com", 9999), servers);
        Assert.DoesNotContain(("badentry", 0), servers);
    }

    [Fact]
    public void Resolve_OverlappingCustom_Deduplicated()
    {
        // 自定义条目与默认服务器重复 → Distinct 后不增加数量
        var servers = NewProvider().Resolve("stun.l.google.com:19302").ToList();

        Assert.Equal(StunServerProvider.DefaultServers.Length, servers.Count);
    }
}
