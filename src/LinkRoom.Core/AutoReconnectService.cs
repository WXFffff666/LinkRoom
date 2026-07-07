using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

/// <summary>
/// Implements exponential backoff auto-reconnect logic.
/// Triggers on EasyTier process exit or RPC timeout.
/// Stops on user disconnect or max attempts exhausted.
/// </summary>
public sealed class AutoReconnectService
{
    private readonly ILogger<AutoReconnectService> _logger;
    private readonly ConnectionStateMachine _stateMachine;
    private readonly Func<CancellationToken, Task> _reconnectAction;

    private CancellationTokenSource? _reconnectCts;
    private int _attemptCount;

    /// <summary>Maximum reconnect attempts before giving up.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Maximum backoff delay in seconds.</summary>
    public int MaxBackoffSeconds { get; set; } = 30;

    /// <summary>Whether auto-reconnect is currently active.</summary>
    public bool IsReconnecting => _reconnectCts != null && !_reconnectCts.IsCancellationRequested;

    public AutoReconnectService(
        ConnectionStateMachine stateMachine,
        Func<CancellationToken, Task> reconnectAction,
        ILogger<AutoReconnectService> logger)
    {
        _stateMachine = stateMachine;
        _reconnectAction = reconnectAction;
        _logger = logger;

        _stateMachine.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, (ConnectionState Old, ConnectionState New) e)
    {
        if (e.New == ConnectionState.Reconnecting)
        {
            _ = StartReconnectLoop();
        }
        else if (e.New is ConnectionState.Disconnected or ConnectionState.Idle)
        {
            CancelReconnect();
        }
    }

    private async Task StartReconnectLoop()
    {
        CancelReconnect();
        _reconnectCts = new CancellationTokenSource();
        _attemptCount = 0;
        var ct = _reconnectCts.Token;

        _logger.LogInformation("Auto-reconnect started (max {Max} attempts, cap {Cap}s)",
            MaxAttempts, MaxBackoffSeconds);

        while (_attemptCount < MaxAttempts && !ct.IsCancellationRequested)
        {
            _attemptCount++;

            // Exponential backoff: 1s, 2s, 4s, 8s, 16s... capped at MaxBackoffSeconds
            var delay = Math.Min(Math.Pow(2, _attemptCount - 1), MaxBackoffSeconds);
            // Add jitter (±25%)
            var jitter = delay * 0.25 * (Random.Shared.NextDouble() * 2 - 1);
            var waitMs = (int)((delay + jitter) * 1000);

            _logger.LogInformation("Reconnect attempt {Attempt}/{Max} in {Delay:F1}s",
                _attemptCount, MaxAttempts, waitMs / 1000.0);

            try
            {
                await Task.Delay(waitMs, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Reconnect cancelled");
                return;
            }

            try
            {
                await _reconnectAction(ct);
                _stateMachine.ReconnectSucceeded();
                _logger.LogInformation("Reconnect succeeded on attempt {Attempt}", _attemptCount);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnect attempt {Attempt} failed", _attemptCount);
            }
        }

        if (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Max reconnect attempts ({Max}) exhausted", MaxAttempts);
            _stateMachine.ReconnectFailed();
        }
    }

    private void CancelReconnect()
    {
        if (_reconnectCts != null)
        {
            _reconnectCts.Cancel();
            _reconnectCts.Dispose();
            _reconnectCts = null;
        }
        _attemptCount = 0;
    }
}
