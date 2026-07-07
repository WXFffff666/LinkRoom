using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

/// <summary>
/// Manages the connection lifecycle state machine.
/// States: Idle → Detecting → Connecting → Connected (+ Monitoring) → Reconnecting → Disconnected.
/// Transitions are triggered by user actions, network detection results, and EasyTier events.
/// Does NOT poll in a tight loop.
/// </summary>
public sealed class ConnectionStateMachine
{
    private readonly ILogger<ConnectionStateMachine> _logger;
    private ConnectionState _currentState = ConnectionState.Idle;
    private readonly object _lock = new();

    public ConnectionState CurrentState
    {
        get { lock (_lock) return _currentState; }
        private set
        {
            lock (_lock)
            {
                if (_currentState == value) return;
                var old = _currentState;
                _currentState = value;
                _logger.LogInformation("State: {Old} → {New}", old, value);
                StateChanged?.Invoke(this, (old, value));
            }
        }
    }

    public event EventHandler<(ConnectionState Old, ConnectionState New)>? StateChanged;

    public ConnectionStateMachine(ILogger<ConnectionStateMachine> logger)
    {
        _logger = logger;
    }

    /// <summary>User clicked Connect.</summary>
    public void UserConnect()
    {
        if (CurrentState != ConnectionState.Idle && CurrentState != ConnectionState.Disconnected)
        {
            _logger.LogWarning("Cannot connect from state {State}", CurrentState);
            return;
        }
        CurrentState = ConnectionState.DetectingNetwork;
    }

    /// <summary>Network detection completed successfully.</summary>
    public void DetectionComplete()
    {
        if (CurrentState != ConnectionState.DetectingNetwork) return;
        CurrentState = ConnectionState.Connecting;
    }

    /// <summary>Network detection failed.</summary>
    public void DetectionFailed()
    {
        if (CurrentState != ConnectionState.DetectingNetwork) return;
        CurrentState = ConnectionState.Disconnected;
    }

    /// <summary>EasyTier core is running and CLI is responsive.</summary>
    public void EasyTierReady()
    {
        if (CurrentState != ConnectionState.Connecting) return;
        CurrentState = ConnectionState.Connected;
    }

    /// <summary>Peer list polling — transitions to Monitoring after first successful peer query.</summary>
    public void Monitoring()
    {
        if (CurrentState != ConnectionState.Connected) return;
        CurrentState = ConnectionState.Monitoring;
    }

    /// <summary>EasyTier process exited or RPC timeout detected.</summary>
    public void ConnectionLost()
    {
        if (CurrentState is ConnectionState.Connected or ConnectionState.Monitoring)
        {
            CurrentState = ConnectionState.Reconnecting;
        }
    }

    /// <summary>Reconnect attempt succeeded.</summary>
    public void ReconnectSucceeded()
    {
        if (CurrentState != ConnectionState.Reconnecting) return;
        CurrentState = ConnectionState.Connected;
    }

    /// <summary>Reconnect attempts exhausted.</summary>
    public void ReconnectFailed()
    {
        if (CurrentState != ConnectionState.Reconnecting) return;
        CurrentState = ConnectionState.Disconnected;
    }

    /// <summary>User clicked Disconnect.</summary>
    public void UserDisconnect()
    {
        if (CurrentState is ConnectionState.Idle or ConnectionState.Disconnected) return;
        CurrentState = ConnectionState.Disconnected;
    }

    /// <summary>Reset to Idle (e.g., after cleanup).</summary>
    public void Reset()
    {
        CurrentState = ConnectionState.Idle;
    }
}

/// <summary>
/// Connection lifecycle states.
/// </summary>
public enum ConnectionState
{
    Idle,
    DetectingNetwork,
    Connecting,
    Connected,
    Monitoring,
    Reconnecting,
    Disconnected,
}
