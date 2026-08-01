namespace LinkRoom.Core;

/// <summary>
/// Single reconnect orchestrator (BUG-5).
/// Serializes all reconnect paths so at most one reconnect executes at any time,
/// and coalesces requests that arrive while a reconnect is already running:
/// the later caller either skips outright (one is in flight) or skips because the
/// process has already been restored by the earlier reconnect.
/// </summary>
public sealed class ReconnectOrchestrator
{
    readonly SemaphoreSlim _gate = new(1, 1);
    int _executing;

    /// <summary>
    /// Runs <paramref name="reconnect"/> at most once per burst of concurrent requests.
    /// A call returns without running when:
    /// - another reconnect is currently executing (in-flight coalescing), or
    /// - <paramref name="isHealthy"/> reports the process is already running again
    ///   (a concurrent reconnect already restored it — retrying would kill it).
    /// A request that arrives after a *failed* reconnect still runs: it is queued
    /// behind the failed one and retries, which is the intended backstop behavior.
    /// </summary>
    public async Task RunOnceAsync(Func<Task> reconnect, Func<bool> isHealthy)
    {
        if (Volatile.Read(ref _executing) != 0) return; // coalesce: one is in flight
        await _gate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _executing) != 0) return; // lost the race — coalesce
            if (isHealthy()) return;                        // already recovered — redundant
            Volatile.Write(ref _executing, 1);
            try
            {
                await reconnect();
            }
            finally
            {
                Volatile.Write(ref _executing, 0);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
