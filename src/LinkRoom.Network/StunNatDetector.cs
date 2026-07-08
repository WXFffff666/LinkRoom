using STUN.Client;
using STUN.Enums;
using System.Net;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Network;

/// <summary>
/// Stun.Net-based NAT type detector.
/// Uses STUN protocol (RFC 5389 / RFC 5780) to determine NAT mapping and filtering behavior.
/// </summary>
public sealed class StunNatDetector : INatDetector
{
    private readonly ILogger<StunNatDetector> _logger;

    private static readonly (string Host, int Port)[] DefaultStunServers =
    [
        ("stun.l.google.com", 19302),
        ("stun1.l.google.com", 19302),
        ("stun2.l.google.com", 19302),
        ("stun3.l.google.com", 19302),
        ("stun4.l.google.com", 19302),
        ("stun.syncthing.net", 3478),
        ("stun.nextcloud.com", 3478),
    ];

    public StunNatDetector(ILogger<StunNatDetector> logger)
    {
        _logger = logger;
    }

    public async Task<NatDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        var result = new NatDetectionResult
        {
            NatType = Network.NatType.Unknown,
            StunReachable = false,
        };

        try
        {
            var host = await Dns.GetHostEntryAsync(Dns.GetHostName(), cancellationToken);
            result = result with
            {
                LocalIPv4 = host.AddressList.FirstOrDefault(
                    a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString(),
                LocalIPv6 = host.AddressList.FirstOrDefault(
                    a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)?.ToString(),
            };
        }
        catch { }

        foreach (var (host, port) in DefaultStunServers)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var r = await ProbeServerAsync(host, port, cancellationToken);
                if (r != null)
                {
                    result = result with
                    {
                        NatType = r.NatType,
                        PublicIPv4 = r.PublicIPv4 ?? result.PublicIPv4,
                        PublicIPv6 = r.PublicIPv6 ?? result.PublicIPv6,
                        StunReachable = true,
                        UdpReachable = r.NatType != Network.NatType.Blocked,
                    };
                    _logger.LogInformation("STUN [{Host}:{Port}] => {NatType}, IP={Ip}",
                        host, port, result.NatType, result.PublicIPv4);
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "STUN {Host}:{Port} unreachable", host, port);
            }
        }

        if (!result.StunReachable)
            _logger.LogWarning("All STUN servers unreachable.");

        return result;
    }

    private async Task<NatDetectionResult?> ProbeServerAsync(string host, int port, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, cts.Token);
            var ipv4 = addrs.First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            var serverEp = new IPEndPoint(ipv4, port);
            var localEp = new IPEndPoint(IPAddress.Any, 0);

            using var client = new StunClient5389UDP(serverEp, localEp);
            client.ReceiveTimeout = TimeSpan.FromSeconds(5);
            await client.QueryAsync(cts.Token);

            var state = client.State;
            if (state.BindingTestResult != BindingTestResult.Success)
                return null;

            var natType = Classify(state.MappingBehavior, state.FilteringBehavior);
            return new NatDetectionResult
            {
                NatType = natType,
                PublicIPv4 = state.PublicEndPoint?.Address?.ToString(),
                StunReachable = true,
                UdpReachable = natType != Network.NatType.Blocked,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static Network.NatType Classify(MappingBehavior m, FilteringBehavior f) => m switch
    {
        MappingBehavior.EndpointIndependent => f switch
        {
            FilteringBehavior.EndpointIndependent => Network.NatType.FullCone,
            FilteringBehavior.AddressDependent => Network.NatType.RestrictedCone,
            FilteringBehavior.AddressAndPortDependent => Network.NatType.PortRestrictedCone,
            _ => Network.NatType.Unknown,
        },
        MappingBehavior.AddressDependent => Network.NatType.Symmetric,
        MappingBehavior.AddressAndPortDependent => Network.NatType.Symmetric,
        MappingBehavior.Fail => Network.NatType.Blocked,
        _ => Network.NatType.Unknown,
    };
}
