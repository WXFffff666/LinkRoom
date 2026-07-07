namespace LinkRoom.Network;

/// <summary>
/// Abstraction for NAT type detection and network information gathering.
/// </summary>
public interface INatDetector
{
    /// <summary>
    /// Performs STUN-based NAT type detection and returns the result.
    /// Should NOT be called on the UI thread (expected to be I/O-bound).
    /// </summary>
    Task<NatDetectionResult> DetectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a NAT type detection.
/// </summary>
public record NatDetectionResult
{
    public NatType NatType { get; init; }
    public string? PublicIPv4 { get; init; }
    public string? PublicIPv6 { get; init; }
    public bool UdpReachable { get; init; }
    public bool StunReachable { get; init; }
    public string? LocalIPv4 { get; init; }
    public string? LocalIPv6 { get; init; }

    public bool HasIPv4 => !string.IsNullOrEmpty(PublicIPv4);
    public bool HasIPv6 => !string.IsNullOrEmpty(PublicIPv6);
    public bool HasAnyConnectivity => StunReachable || UdpReachable;
}

/// <summary>
/// NAT type classification.
/// </summary>
public enum NatType
{
    Unknown,
    OpenInternet,
    FullCone,
    RestrictedCone,
    PortRestrictedCone,
    Symmetric,
    SymmetricUdpFirewall,
    Blocked,
}
