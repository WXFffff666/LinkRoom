namespace LinkRoom.Network;

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
}

public sealed record NetworkSnapshot
{
    public NatType NatType { get; init; }
    public string? PublicIPv4 { get; init; }
    public string? PublicIPv6 { get; init; }
    public bool UdpReachable { get; init; }
    public bool StunReachable { get; init; }
    public string? LocalIPv4 { get; init; }
    public string? LocalIPv6 { get; init; }
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;

    public bool HasIPv4 => !string.IsNullOrEmpty(PublicIPv4);
    public bool HasIPv6 => !string.IsNullOrEmpty(PublicIPv6);
    public bool HasAnyConnectivity => StunReachable || UdpReachable;
    public bool IsFullCone => NatType == NatType.FullCone;
    public bool IsSymmetric => NatType is NatType.Symmetric or NatType.SymmetricUdpFirewall;
}

public interface INatDetector
{
    Task<NatDetectionResult> DetectAsync(CancellationToken cancellationToken = default);
}
