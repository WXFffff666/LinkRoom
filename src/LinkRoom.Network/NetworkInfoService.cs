using Microsoft.Extensions.Logging;

namespace LinkRoom.Network;

public sealed class NetworkInfoService
{
    readonly StunNatDetector _detector;
    readonly ILogger<NetworkInfoService> _logger;
    string? _customStun;

    public NetworkInfoService(StunNatDetector detector, ILogger<NetworkInfoService> logger)
    {
        _detector = detector;
        _logger = logger;
    }

    public void SetCustomStunServers(string? csv)
    {
        _customStun = csv;
        _detector.SetCustomServers(csv);
    }

    public async Task<NetworkSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting network detection...");
        _detector.SetCustomServers(_customStun);

        NatDetectionResult detection;
        try
        {
            detection = await _detector.DetectAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network detection failed");
            return new NetworkSnapshot { NatType = NatType.Unknown, CapturedAt = DateTime.UtcNow };
        }

        var snapshot = new NetworkSnapshot
        {
            NatType = detection.NatType,
            PublicIPv4 = detection.PublicIPv4,
            PublicIPv6 = detection.PublicIPv6,
            UdpReachable = detection.UdpReachable,
            StunReachable = detection.StunReachable,
            LocalIPv4 = detection.LocalIPv4,
            LocalIPv6 = detection.LocalIPv6,
            CapturedAt = DateTime.UtcNow,
        };

        _logger.LogInformation("Detection: {Nat}, v4={V4}, v6={V6}",
            snapshot.NatType, snapshot.PublicIPv4 ?? "-", snapshot.PublicIPv6 ?? "-");
        return snapshot;
    }
}
