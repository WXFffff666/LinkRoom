using Microsoft.Extensions.Logging;

namespace LinkRoom.Network;

/// <summary>
/// Orchestrates one-shot network detection: runs Stun.Net, enumerates interfaces,
/// and produces a NetworkSnapshot. Does NOT poll continuously.
/// </summary>
public sealed class NetworkInfoService
{
    private readonly INatDetector _detector;
    private readonly ILogger<NetworkInfoService> _logger;

    public NetworkInfoService(INatDetector detector, ILogger<NetworkInfoService> logger)
    {
        _detector = detector;
        _logger = logger;
    }

    /// <summary>
    /// Runs a full network detection pass and returns a snapshot.
    /// </summary>
    public async Task<NetworkSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting network detection...");

        NatDetectionResult detection;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15)); // total detection budget

            detection = await _detector.DetectAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network detection failed");
            return new NetworkSnapshot
            {
                NatType = NatType.Unknown,
                CapturedAt = DateTime.UtcNow,
            };
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

        _logger.LogInformation("Detection complete: {NatType}, IPv4={IPv4}, IPv6={IPv6}, UDP={Udp}",
            snapshot.NatType, snapshot.PublicIPv4 ?? "none", snapshot.PublicIPv6 ?? "none", snapshot.UdpReachable);

        return snapshot;
    }
}
