using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

public sealed class ProcessGuardian
{
    readonly EasyTierProcessService _proc;
    readonly ILogger<ProcessGuardian> _logger;
    CancellationTokenSource? _cts;

    public ProcessGuardian(EasyTierProcessService proc, ILogger<ProcessGuardian> logger)
    {
        _proc = proc;
        _logger = logger;
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

    async Task WatchAsync(Func<Task> onUnhealthy, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(15000, ct);
                if (!_proc.IsRunning && _proc.ProcessId == null)
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
