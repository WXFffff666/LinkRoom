using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Network;

/// <summary>
/// Provides STUN server lists: defaults, custom, remote (always-online-stun), and health cache.
/// </summary>
public sealed class StunServerProvider
{
    public static string? CachePathOverride { get; set; }

    readonly ILogger<StunServerProvider> _logger;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static readonly (string Host, int Port)[] DefaultServers =
    [
        ("stun.l.google.com", 19302),
        ("stun1.l.google.com", 19302),
        ("stun2.l.google.com", 19302),
        ("stun.syncthing.net", 3478),
        ("stun.hot-chilli.net", 3478),
        ("stun.fitauto.ru", 3478),
        ("stun.internetcalls.com", 3478),
        ("stun.voip.aebc.com", 3478),
        ("stun.voipbuster.com", 3478),
        ("stun.voipstunt.com", 3478),
    ];

    const string RemoteListUrl =
        "https://raw.githubusercontent.com/pradt2/always-online-stun/master/valid_hosts.txt";

    List<(string Host, int Port)>? _healthy;
    DateTime _healthyAt;

    public StunServerProvider(ILogger<StunServerProvider> logger) => _logger = logger;

    public IEnumerable<(string Host, int Port)> Resolve(string? customCsv)
    {
        var list = new List<(string, int)>();
        if (!string.IsNullOrWhiteSpace(customCsv))
        {
            foreach (var entry in customCsv.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = entry.Trim();
                var parts = t.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out var port))
                    list.Add((parts[0], port));
            }
        }

        if (_healthy != null && DateTime.UtcNow - _healthyAt < TimeSpan.FromHours(1))
            list.AddRange(_healthy);
        else
            list.AddRange(DefaultServers);

        return list.Distinct();
    }

    public async Task RefreshRemoteListAsync(CancellationToken ct = default)
    {
        try
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("LinkRoom");
            var text = await _http.GetStringAsync(RemoteListUrl, ct);
            var servers = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !l.StartsWith('#'))
                .Select(l =>
                {
                    var p = l.Split(':');
                    return p.Length == 2 && int.TryParse(p[1], out var port) ? (p[0], port) : default;
                })
                .Where(x => x != default)
                .Take(20)
                .ToList();

            if (servers.Count > 0)
            {
                _healthy = servers;
                _healthyAt = DateTime.UtcNow;
                _logger.LogInformation("Loaded {Count} remote STUN servers", servers.Count);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(StunCachePath())!);
                    await File.WriteAllTextAsync(StunCachePath(),
                        JsonSerializer.Serialize(servers.Select(s => $"{s.Item1}:{s.Item2}")),
                        ct);
                }
                catch { /* cache optional */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch remote STUN list");
            LoadCached();
        }
    }

    public async Task<IReadOnlyList<(string Host, int Port, bool Ok, string? Detail)>> HealthCheckAsync(
        IEnumerable<(string Host, int Port)> servers,
        NatProbeService probe,
        CancellationToken ct = default)
    {
        var tasks = servers.Select(async s =>
        {
            var r = await probe.ProbeSingleAsync(s.Host, s.Port, fullBehavior: false, ct);
            return (s.Host, s.Port, r != null, r?.NatType.ToString());
        });
        return await Task.WhenAll(tasks);
    }

    void LoadCached()
    {
        try
        {
            var path = StunCachePath();
            if (!File.Exists(path)) return;
            var arr = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path));
            if (arr == null) return;
            _healthy = arr.Select(l =>
            {
                var p = l.Split(':');
                return p.Length == 2 && int.TryParse(p[1], out var port) ? (p[0], port) : default;
            }).Where(x => x != default).ToList();
            _healthyAt = DateTime.UtcNow;
        }
        catch { /* ignore */ }
    }

    static string StunCachePath() =>
        StunServerProvider.CachePathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkRoom", "config", "stun-servers.json");
}
