using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

/// <summary>
/// Health snapshot of the EasyTier core subprocess, as observed by the guardian.
/// </summary>
public interface IProcessHealth
{
    /// <summary>Whether the process object is live and has not exited.</summary>
    bool IsRunning { get; }

    /// <summary>The OS process ID, or null when no process object exists.</summary>
    int? ProcessId { get; }
}

/// <summary>
/// Polls EasyTier core health and triggers recovery when the process is dead.
/// </summary>
public sealed class ProcessGuardian
{
    readonly IProcessHealth _proc;
    readonly ILogger<ProcessGuardian> _logger;
    readonly TimeSpan _pollInterval;
    CancellationTokenSource? _cts;

    public ProcessGuardian(IProcessHealth proc, ILogger<ProcessGuardian> logger)
        : this(proc, logger, TimeSpan.FromSeconds(15))
    {
    }

    /// <summary>Constructor with an explicit poll interval (test seam).</summary>
    public ProcessGuardian(IProcessHealth proc, ILogger<ProcessGuardian> logger, TimeSpan pollInterval)
    {
        _proc = proc;
        _logger = logger;
        _pollInterval = pollInterval;
    }

    public void Start(Func<Task> onUnhealthy)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = WatchAsync(onUnhealthy, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// BUG-5: the previous condition (<c>!_proc.IsRunning &amp;&amp; _proc.ProcessId == null</c>)
    /// could never be true — after the process exits, <c>ProcessId</c> still returns the
    /// stale, non-null PID, so the guardian never fired. A process is unhealthy when it is
    /// either missing (no process object) or exited.
    /// </summary>
    static bool IsUnhealthy(IProcessHealth proc) => proc.ProcessId == null || !proc.IsRunning;

    async Task WatchAsync(Func<Task> onUnhealthy, CancellationToken ct)
    {
        // Debounce: remember the last observed health so a dead process triggers
        // recovery exactly once per dead episode instead of every poll tick.
        var wasDead = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct);
                var dead = IsUnhealthy(_proc);
                var trigger = dead && !wasDead;
                // Update the debounce state BEFORE the callback: even when recovery
                // fails (callback throws), the dead episode still counts as observed,
                // so the guardian does not re-trigger on every subsequent tick.
                wasDead = dead;
                if (trigger)
                {
                    _logger.LogWarning("EasyTier process not running — triggering recovery");
                    await onUnhealthy();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogDebug(ex, "Guardian tick error"); }
        }
    }
}
