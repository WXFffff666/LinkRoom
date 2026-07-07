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
        var output = new System.Text.StringBuilder();

        process.Start();

        // Read with timeout — limit output to 10MB to prevent memory exhaustion
        var readTask = process.StandardOutput.ReadToEndAsync();
        var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(15), ct));

        if (completed != readTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("easytier-cli command timed out after 15 seconds.");
        }

        var stdout = await readTask;
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            _logger.LogWarning("easytier-cli exited with code {Code}: {Error}", process.ExitCode, stderr);
            throw new InvalidOperationException($"easytier-cli failed (exit code {process.ExitCode})");
        }

        // Limit output size
        if (stdout.Length > 10 * 1024 * 1024)
        {
            throw new InvalidOperationException($"easytier-cli output exceeded 10 MB limit ({stdout.Length} bytes)");
        }

        return stdout;
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
    public double? LatencyMs { get; init; }

    [JsonPropertyName("loss_rate")]
    public double? LossRate { get; init; }

    [JsonPropertyName("ipv4")]
    public string? IPv4 { get; init; }

    [JsonPropertyName("ipv6")]
    public string? IPv6 { get; init; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; init; }

    [JsonPropertyName("instance_id")]
    public string? InstanceId { get; init; }
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
