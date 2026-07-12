using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

public sealed class PathSelectionStrategy
{
    readonly ILogger<PathSelectionStrategy> _logger;

    public PathSelectionStrategy(ILogger<PathSelectionStrategy> logger) => _logger = logger;

    public PathRecommendation Evaluate(NetworkSnapshot? snapshot, AdvancedOptions advanced)
    {
        var rec = new PathRecommendation();

        if (snapshot == null || !snapshot.StunReachable)
        {
            _logger.LogWarning("No network snapshot — default path");
            rec.Strategy = "default";
            rec.Warnings.Add("网络检测不可用，连接可能不稳定。");
        }
        else
        {
            switch (snapshot.NatType)
            {
                case NatType.OpenInternet:
                case NatType.FullCone:
                case NatType.RestrictedCone:
                case NatType.PortRestrictedCone:
                    rec.Strategy = "udp-hole-punch";
                    rec.TomlFlags.Add("disable_tcp_hole_punching = true");
                    break;
                case NatType.Symmetric:
                case NatType.SymmetricUdpFirewall:
                    rec.Strategy = "tcp-hole-punch";
                    rec.TomlFlags.Add("disable_udp_hole_punching = true");
                    if (!advanced.IsUpnpDisabled)
                        rec.Warnings.Add("已启用 UPnP，将在路由器上映射端口。");
                    else
                        rec.Warnings.Add("对称型 NAT：建议启用 UPnP 或共享节点中继。");
                    break;
                default:
                    rec.Strategy = "auto";
                    break;
            }

            if (snapshot is { HasIPv6: true, HasIPv4: false } || advanced.Ipv6Only)
            {
                rec.Strategy += "+ipv6";
                rec.TomlFlags.Add("enable_ipv6 = true");
            }

            if (advanced.Ipv6Only)
            {
                rec.Strategy += "+ipv6-only";
                rec.TomlFlags.Add("disable_ipv4 = true");
            }
        }

        if (advanced.IsSharedNodeEnabled)
        {
            if (snapshot is { HasAnyConnectivity: false } or { NatType: NatType.Blocked })
            {
                rec.Strategy = "relay-via-shared-node";
                rec.Warnings.Add("直连不可用，将通过共享节点中继。");
            }
        }

        // LAN mode: virtual NIC + UDP broadcast relay for MC etc.
        if (advanced.UseLanMode)
        {
            rec.TomlFlags.Add("# LAN mode: virtual NIC for game discovery");
            rec.TomlFlags.Add("enable_udp_broadcast_relay = true");
            rec.Flags.Add("--enable-kcp-proxy=false");
        }
        else
        {
            rec.TomlFlags.Add("# Lightweight mode: no virtual NIC");
            rec.Flags.Add("--no-tun");
        }

        rec.Flags.Add("--lazy-p2p");

        if (advanced.IsHostMode)
            rec.Flags.Add("--need-p2p");

        _logger.LogInformation("Path: {Strategy} lan={Lan} host={Host}",
            rec.Strategy, advanced.UseLanMode, advanced.IsHostMode);

        return rec;
    }
}

public sealed record PathRecommendation
{
    public string Strategy { get; set; } = "auto";
    public List<string> Flags { get; init; } = [];
    public List<string> TomlFlags { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
