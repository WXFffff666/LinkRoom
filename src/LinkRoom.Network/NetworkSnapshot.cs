namespace LinkRoom.Network;

/// <summary>
/// Immutable snapshot of the current network environment including NAT type,
/// public IPs, and local connectivity information.
/// </summary>
public sealed record NetworkSnapshot
{
    /// <summary>The detected NAT type.</summary>
    public NatType NatType { get; init; }

    /// <summary>Public IPv4 address as seen by STUN servers, or null if undetected.</summary>
    public string? PublicIPv4 { get; init; }

    /// <summary>Public IPv6 address as seen by STUN servers, or null if undetected.</summary>
    public string? PublicIPv6 { get; init; }

    /// <summary>Whether UDP communication to the internet is possible.</summary>
    public bool UdpReachable { get; init; }

    /// <summary>Whether any STUN server was reachable.</summary>
    public bool StunReachable { get; init; }

    /// <summary>Local IPv4 address on the active network interface.</summary>
    public string? LocalIPv4 { get; init; }

    /// <summary>Local IPv6 address on the active network interface.</summary>
    public string? LocalIPv6 { get; init; }

    /// <summary>Timestamp when this snapshot was captured.</summary>
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;

    // Convenience properties for path selection
    public bool HasIPv4 => !string.IsNullOrEmpty(PublicIPv4);
    public bool HasIPv6 => !string.IsNullOrEmpty(PublicIPv6);
    public bool HasAnyConnectivity => StunReachable || UdpReachable;
    public bool IsFullCone => NatType == NatType.FullCone;
    public bool IsSymmetric => NatType is NatType.Symmetric or NatType.SymmetricUdpFirewall;
}
