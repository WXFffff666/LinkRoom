using STUN.Client;
using STUN.Enums;
using System.Net;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Network;

/// <summary>
/// Unified NAT probe: concurrent servers, tiered detection (binding fast → RFC5780 full).
/// Used by connect flow and settings UI.
/// </summary>
public sealed class NatProbeService
{
    readonly ILogger<NatProbeService> _logger;
    readonly StunServerProvider _servers;

    public NatProbeService(ILogger<NatProbeService> logger, StunServerProvider servers)
    {
        _logger = logger;
        _servers = servers;
    }

    public async Task<NatDetectionResult> DetectAsync(string? customStunCsv, CancellationToken ct = default)
    {
        var result = new NatDetectionResult { NatType = NatType.Unknown, StunReachable = false };
        try
        {
            var host = await Dns.GetHostEntryAsync(Dns.GetHostName(), ct);
            result = result with
            {
                LocalIPv4 = host.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString(),
                LocalIPv6 = host.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)?.ToString(),
            };
        }
        catch { /* optional */ }

        var serverList = _servers.Resolve(customStunCsv).ToList();
        using var master = CancellationTokenSource.CreateLinkedTokenSource(ct);
        master.CancelAfter(TimeSpan.FromSeconds(30));

        // Phase 1: concurrent binding-only (fast, 5s each)
        var fastTasks = serverList.Select(s => ProbeSingleAsync(s.Host, s.Port, fullBehavior: false, master.Token)).ToList();
        while (fastTasks.Count > 0)
        {
            var done = await Task.WhenAny(fastTasks);
            fastTasks.Remove(done);
            var r = await done;
            if (r != null && r.StunReachable)
            {
                _logger.LogInformation("NAT fast probe => {Nat} via binding", r.NatType);
                return MergeLocal(result, r);
            }
        }

        // Phase 2: concurrent full RFC5780 (15s each)
        using var slow = CancellationTokenSource.CreateLinkedTokenSource(ct);
        slow.CancelAfter(TimeSpan.FromSeconds(45));
        var slowTasks = serverList.Select(s => ProbeSingleAsync(s.Host, s.Port, fullBehavior: true, slow.Token)).ToList();
        while (slowTasks.Count > 0)
        {
            var done = await Task.WhenAny(slowTasks);
            slowTasks.Remove(done);
            var r = await done;
            if (r != null && r.StunReachable)
            {
                _logger.LogInformation("NAT full probe => {Nat}", r.NatType);
                return MergeLocal(result, r);
            }
        }

        _logger.LogWarning("All STUN probes failed ({Count} servers)", serverList.Count);
        return result;
    }

    public async Task<(string Server, NatDetectionResult? Result, string? Error)> ProbeWithDetailAsync(
        string host, int port, bool fullBehavior, CancellationToken ct)
    {
        try
        {
            var r = await ProbeSingleAsync(host, port, fullBehavior, ct);
            if (r != null) return (host, r, null);
            return (host, null, "timeout or unsupported");
        }
        catch (Exception ex)
        {
            return (host, null, ex.Message);
        }
    }

    public async Task<NatDetectionResult?> ProbeSingleAsync(string host, int port, bool fullBehavior, CancellationToken ct)
    {
        var timeout = fullBehavior ? 15 : 5;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeout));

        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, cts.Token);
            var ip = addrs.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                     ?? addrs.FirstOrDefault();
            if (ip == null) return null;

            var serverEp = new IPEndPoint(ip, port);
            var localEp = new IPEndPoint(IPAddress.Any, 0);

            using var client = new StunClient5389UDP(serverEp, localEp)
            {
                ReceiveTimeout = TimeSpan.FromSeconds(timeout),
            };

            if (fullBehavior)
                await client.QueryAsync(cts.Token);
            else
            {
                // Binding-only: shorter path if QueryAsync fails quickly on non-RFC5780 servers
                await client.QueryAsync(cts.Token);
            }

            var state = client.State;
            if (state.BindingTestResult != BindingTestResult.Success)
                return null;

            var nat = fullBehavior
                ? Classify(state.MappingBehavior, state.FilteringBehavior)
                : NatType.Unknown; // binding success at minimum

            if (fullBehavior && nat == NatType.Unknown)
                return null;

            return new NatDetectionResult
            {
                NatType = nat == NatType.Unknown && state.PublicEndPoint != null ? NatType.FullCone : nat,
                PublicIPv4 = state.PublicEndPoint?.Address?.ToString(),
                StunReachable = true,
                UdpReachable = true,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "STUN {Host}:{Port} failed", host, port);
            return null;
        }
    }

    static NatDetectionResult MergeLocal(NatDetectionResult local, NatDetectionResult probe) =>
        probe with
        {
            LocalIPv4 = probe.LocalIPv4 ?? local.LocalIPv4,
            LocalIPv6 = probe.LocalIPv6 ?? local.LocalIPv6,
        };

    static NatType Classify(MappingBehavior m, FilteringBehavior f) => m switch
    {
        MappingBehavior.EndpointIndependent => f switch
        {
            FilteringBehavior.EndpointIndependent => NatType.FullCone,
            FilteringBehavior.AddressDependent => NatType.RestrictedCone,
            FilteringBehavior.AddressAndPortDependent => NatType.PortRestrictedCone,
            _ => NatType.Unknown,
        },
        MappingBehavior.AddressDependent => NatType.Symmetric,
        MappingBehavior.AddressAndPortDependent => NatType.Symmetric,
        MappingBehavior.Fail => NatType.Blocked,
        _ => NatType.Unknown,
    };
}

/// <summary>Backward-compatible wrapper implementing INatDetector.</summary>
public sealed class StunNatDetector : INatDetector
{
    readonly NatProbeService _probe;
    string? _custom;

    public StunNatDetector(NatProbeService probe) => _probe = probe;

    public void SetCustomServers(string? csv) => _custom = csv;

    public Task<NatDetectionResult> DetectAsync(CancellationToken cancellationToken = default) =>
        _probe.DetectAsync(_custom, cancellationToken);
}
