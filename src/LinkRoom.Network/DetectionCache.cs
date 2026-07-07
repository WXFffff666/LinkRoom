using Microsoft.Extensions.Logging;

namespace LinkRoom.Network;

/// <summary>
/// Caches the last NetworkSnapshot and refreshes only when explicitly invalidated
/// or when the TTL expires. Does NOT poll in a background loop.
/// </summary>
public sealed class DetectionCache
{
    private readonly NetworkInfoService _service;
    private readonly ILogger<DetectionCache> _logger;

    private NetworkSnapshot? _cached;
    private readonly object _lock = new();

    /// <summary>
    /// How long before a cached result is considered stale (default 10 minutes).
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(10);

    public DetectionCache(NetworkInfoService service, ILogger<DetectionCache> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Returns the cached snapshot if valid, otherwise runs fresh detection.
    /// </summary>
    public async Task<NetworkSnapshot> GetAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_cached != null && DateTime.UtcNow - _cached.CapturedAt < Ttl)
            {
                _logger.LogDebug("Detection cache hit (age: {Age}s)",
                    (DateTime.UtcNow - _cached.CapturedAt).TotalSeconds);
                return _cached;
            }
        }

        _logger.LogInformation("Detection cache miss — running fresh detection");

        var snapshot = await _service.CaptureAsync(ct);

        lock (_lock)
        {
            _cached = snapshot;
        }

        return snapshot;
    }

    /// <summary>
    /// Force-invalidates the cache so the next GetAsync runs fresh detection.
    /// </summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _cached = null;
        }
        _logger.LogDebug("Detection cache invalidated");
    }

    /// <summary>
    /// Returns the last cached snapshot without triggering fresh detection.
    /// Returns null if nothing is cached.
    /// </summary>
    public NetworkSnapshot? GetCached()
    {
        lock (_lock)
        {
            return _cached;
        }
    }
}
