using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

/// <summary>
/// Queries a running easytier-core instance via the easytier-cli subprocess.
/// Uses --output json for structured output. Never opens a raw TCP socket
/// to the RPC portal (EasyTier uses a framed protobuf tunnel, not plain JSON-RPC).
/// </summary>
public sealed class EasyTierCliClient
{
    private readonly string _easytierCliPath;
    private readonly string _rpcPortal;
    private readonly ILogger<EasyTierCliClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private const int MaxOutputChars = 10 * 1024 * 1024; // 10 MB cap, enforced while streaming

    public EasyTierCliClient(string easytierCliPath, string rpcPortal, ILogger<EasyTierCliClient> logger)
    {
        _easytierCliPath = easytierCliPath;
        _rpcPortal = rpcPortal;
        _logger = logger;
    }

    public async Task<PeerInfo[]> GetPeersAsync(CancellationToken ct = default)
    {
        var json = await RunCliAsync("peer", ct);
        return Deserialize<PeerInfo[]>(json) ?? [];
    }

    public async Task<RouteInfo[]> GetRoutesAsync(CancellationToken ct = default)
    {
        var json = await RunCliAsync("route", ct);
        return Deserialize<RouteInfo[]>(json) ?? [];
    }

    public async Task<NodeInfo?> GetNodeInfoAsync(CancellationToken ct = default)
    {
        var json = await RunCliAsync("node info", ct);
        return Deserialize<NodeInfo>(json);
    }

    /// <summary>
    /// Reads the instance's cumulative rx/tx byte counters from
    /// `stats show --output json` (spike: SPIKE-TRAFFIC.md §2.4). Prefers
    /// traffic_bytes_self_rx/self_tx (own traffic, not relayed-for-others);
    /// falls back to traffic_bytes_rx/tx if the self variants are absent.
    /// Returns null when the counters are unavailable (older core, degraded mode).
    /// </summary>
    public async Task<TrafficStats?> GetTrafficStatsAsync(CancellationToken ct = default)
    {
        var json = await RunCliAsync("stats show", ct);
        var metrics = Deserialize<TrafficMetric[]>(json);
        if (metrics == null || metrics.Length == 0) return null;

        ulong? rx = PickValue(metrics, "traffic_bytes_self_rx", "traffic_bytes_rx");
        ulong? tx = PickValue(metrics, "traffic_bytes_self_tx", "traffic_bytes_tx");
        if (rx == null || tx == null) return null;
        return new TrafficStats(rx.Value, tx.Value);
    }

    private static ulong? PickValue(TrafficMetric[] metrics, params string[] names)
    {
        foreach (var name in names)
        {
            var v = metrics.FirstOrDefault(m => m.Name == name)?.Value;
            if (v != null) return v;
        }
        return null;
    }

    private async Task<string> RunCliAsync(string subcommand, CancellationToken ct)
    {
        if (!File.Exists(_easytierCliPath))
            throw new FileNotFoundException($"easytier-cli not found: {_easytierCliPath}");

        // --output json goes BEFORE the subcommand (clap requires this)
        var args = $"--output json --rpc-portal {_rpcPortal} {subcommand}";

        _logger.LogDebug("Running easytier-cli: {Args}", args);

        var psi = new ProcessStartInfo
        {
            FileName = _easytierCliPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        process.Start();

        // Drain stdout and stderr concurrently: a child that floods stderr past
        // the ~4KB pipe buffer would block forever and deadlock WaitForExitAsync
        // if stderr were only read after the process exits (BUG-4).
        var stdoutTask = ReadStdoutAsync(process, stdout, ct);
        var stderrTask = ReadStderrAsync(process, stderr, ct);

        var waitTask = process.WaitForExitAsync(ct);
        try
        {
            var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(15), ct));
            if (completed != waitTask)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("easytier-cli command timed out after 15 seconds.");
            }

            await waitTask;
            await Task.WhenAll(stdoutTask, stderrTask);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("easytier-cli exited with code {Code}: {Error}", process.ExitCode, stderr.ToString());
                throw new InvalidOperationException($"easytier-cli failed (exit code {process.ExitCode})");
            }

            return stdout.ToString();
        }
        finally
        {
            // Ensure no read task is left dangling on any exit path (timeout,
            // size cap, cancellation): killing the tree closes the pipes, which
            // lets both readers finish.
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* preserve primary exception */ }
            try { await waitTask; } catch { /* observe cancellation */ }
        }
    }

    private static async Task ReadStdoutAsync(Process process, System.Text.StringBuilder stdout, CancellationToken ct)
    {
        while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            stdout.Append(line).Append('\n');

            // Enforce the 10MB cap while streaming, before memory can grow
            // unbounded (BUG-3): kill immediately and fail the command.
            if (stdout.Length > MaxOutputChars)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new InvalidOperationException(
                    $"easytier-cli output exceeded 10 MB limit ({stdout.Length} chars)");
            }
        }
    }

    private static async Task ReadStderrAsync(Process process, System.Text.StringBuilder stderr, CancellationToken ct)
    {
        while (await process.StandardError.ReadLineAsync(ct) is { } line)
        {
            // Cap stderr accumulation but always keep draining so the child
            // never blocks on a full pipe.
            if (stderr.Length < MaxOutputChars)
                stderr.Append(line).Append('\n');
        }
    }

    private static T? Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            // If it's a single-instance response wrapped, try unwrapping
            // EasyTier wraps multi-instance output, single-instance is raw
            if (json.TrimStart().StartsWith("["))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(json, JsonOptions);
                }
                catch { /* fall through */ }
            }
            throw new InvalidOperationException($"Failed to parse easytier-cli JSON output: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Peer information from easytier-cli peer --output json.
/// </summary>
public record PeerInfo
{
    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    [JsonPropertyName("instance_name")]
    public string? InstanceName { get; init; }

    [JsonPropertyName("cost")]
    public string? Cost { get; init; }

    [JsonPropertyName("tunnel_proto")]
    public string? TunnelProto { get; init; }

    [JsonPropertyName("nat_type")]
    public string? NatType { get; init; }

    [JsonPropertyName("lat_ms")]
    [JsonConverter(typeof(LenientNumberConverter))]
    public double? LatencyMs { get; init; }

    [JsonPropertyName("loss_rate")]
    [JsonConverter(typeof(LenientNumberConverter))]
    public double? LossRate { get; init; }

    [JsonPropertyName("ipv4")]
    public string? IPv4 { get; init; }

    [JsonPropertyName("ipv6")]
    public string? IPv6 { get; init; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; init; }

    [JsonPropertyName("instance_id")]
    public string? InstanceId { get; init; }

    // NOTE (spike SPIKE-TRAFFIC.md §2.1/§3): easytier-cli serializes rx_bytes/
    // tx_bytes as human-formatted STRINGS ("0 B", "1.5 KB", "-"), not raw byte
    // counts — ulong would throw. Raw u64 counters come from stats show instead
    // (GetTrafficStatsAsync). Kept as string? to mirror the wire format without
    // breaking deserialization of the whole peer array.
    [JsonPropertyName("rx_bytes")]
    public string? RxBytes { get; init; }

    [JsonPropertyName("tx_bytes")]
    public string? TxBytes { get; init; }
}

/// <summary>
/// One metric entry from `easytier-cli stats show --output json` — value is a
/// raw u64 counter (spike SPIKE-TRAFFIC.md §2.4).
/// </summary>
public record TrafficMetric
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("value")]
    public ulong? Value { get; init; }
}

/// <summary>
/// Cumulative rx/tx byte counters of the local instance (from stats show).
/// </summary>
public record TrafficStats(ulong RxBytes, ulong TxBytes);

/// <summary>
/// Tolerates easytier-cli's lenient numeric formatting: JSON numbers, plain
/// string numbers ("12.34"), percent strings ("0.5%" — divided by 100), and
/// "-" (no data) which maps to null. Without this the whole peer array fails
/// to deserialize on the Local row ("-"), a latent bug in the old double?
/// parsing.
/// </summary>
public sealed class LenientNumberConverter : System.Text.Json.Serialization.JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDouble();
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s) || s == "-") return null;
                if (s.EndsWith('%') && double.TryParse(s[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct))
                    return pct / 100.0;
                if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    return v;
                return null;
            default:
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);
}

/// <summary>
/// Route information from easytier-cli route --output json.
/// </summary>
public record RouteInfo
{
    [JsonPropertyName("cidr")]
    public string? Cidr { get; init; }

    [JsonPropertyName("next_hop")]
    public string? NextHop { get; init; }

    [JsonPropertyName("metric")]
    public int? Metric { get; init; }

    [JsonPropertyName("via")]
    public string? Via { get; init; }
}

/// <summary>
/// Node information from easytier-cli node info --output json.
/// </summary>
public record NodeInfo
{
    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    [JsonPropertyName("instance_name")]
    public string? InstanceName { get; init; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; init; }

    [JsonPropertyName("ipv4")]
    public string? IPv4 { get; init; }

    [JsonPropertyName("ipv6")]
    public string? IPv6 { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("instance_id")]
    public string? InstanceId { get; init; }
}
