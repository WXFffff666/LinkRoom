using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

/// <summary>
/// Recommends EasyTier flags and connection strategy based on the NetworkSnapshot.
/// UPnP and shared nodes are only recommended when the user has explicitly opted in.
/// </summary>
public sealed class PathSelectionStrategy
{
    private readonly ILogger<PathSelectionStrategy> _logger;

    public PathSelectionStrategy(ILogger<PathSelectionStrategy> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Evaluates the snapshot and advanced options to produce a path recommendation.
    /// </summary>
    public PathRecommendation Evaluate(NetworkSnapshot? snapshot, AdvancedOptions advanced)
    {
        if (snapshot == null || !snapshot.StunReachable)
        {
            _logger.LogWarning("No network snapshot available — using default path");
            return new PathRecommendation
            {
                Strategy = "default",
                Warnings = ["Network detection unavailable. Connectivity not guaranteed."],
            };
        }

        var rec = new PathRecommendation();

        switch (snapshot.NatType)
        {
            case Network.NatType.OpenInternet:
            case Network.NatType.FullCone:
                rec.Strategy = "udp-hole-punch";
                rec.Flags.Add("--disable-tcp-hole-punching");
                _logger.LogInformation("Path: {Strategy} (NAT: {Nat})", rec.Strategy, snapshot.NatType);
                break;

            case Network.NatType.RestrictedCone:
            case Network.NatType.PortRestrictedCone:
                rec.Strategy = "udp-hole-punch";
                rec.Flags.Add("--disable-tcp-hole-punching");
                _logger.LogInformation("Path: {Strategy} (NAT: {Nat})", rec.Strategy, snapshot.NatType);
                break;

            case Network.NatType.Symmetric:
            case Network.NatType.SymmetricUdpFirewall:
                rec.Strategy = "tcp-hole-punch";
                rec.Flags.Add("--disable-udp-hole-punching");
                if (!advanced.IsUpnpDisabled)
                {
                    rec.Flags.Add("--disable-upnp=false");
                    rec.Warnings.Add("UPnP is enabled. This will open ports on your router.");
                    _logger.LogWarning("UPnP enabled for symmetric NAT traversal");
                }
                else
                {
                    rec.Warnings.Add("UPnP is disabled. Symmetric NAT traversal may fail without it.");
                }
                _logger.LogInformation("Path: {Strategy} (NAT: {Nat})", rec.Strategy, snapshot.NatType);
                break;

            default:
                rec.Strategy = "auto";
                _logger.LogWarning("Unknown NAT type ({Nat}) — using auto strategy", snapshot.NatType);
                break;
        }

        // IPv6 is enabled by default — provide feedback
        if (snapshot is { HasIPv6: true, HasIPv4: false })
        {
            rec.Strategy += "+ipv6";
            rec.Flags.Add("--enable-ipv6");
        }

        // Shared nodes as fallback
        if (!snapshot.HasAnyConnectivity && advanced.IsSharedNodeEnabled)
        {
            rec.Strategy = "relay-via-shared-node";
            rec.Warnings.Add("Direct P2P not possible. Falling back to shared nodes (relay mode).");
            _logger.LogWarning("Falling back to shared nodes (no direct connectivity)");
        }
        else if (snapshot.NatType == Network.NatType.Blocked && advanced.IsSharedNodeEnabled)
        {
            rec.Strategy = "relay-via-shared-node";
            rec.Warnings.Add("UDP blocked. Using shared node relay.");
        }

        // NO --p2p-only — EasyTier handles P2P preference via --lazy-p2p (keeps alive while waiting)
        rec.Flags.Add("--lazy-p2p");
        rec.Flags.Add("--no-tun"); // No virtual NIC needed for game P2P

        return rec;
    }
}

/// <summary>
/// Result of path selection: which strategy to use, flags to apply, and any warnings.
/// </summary>
public sealed record PathRecommendation
{
    public string Strategy { get; set; } = "auto";
    public List<string> Flags { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
