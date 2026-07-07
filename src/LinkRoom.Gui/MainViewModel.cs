using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkRoom.Core;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Gui;

public partial class MainViewModel : ObservableObject
{
    private readonly EasyTierConfigBuilder _configBuilder;
    private readonly EasyTierProcessService _processService;
    private readonly EasyTierCliClient _cliClient;
    private readonly ConnectionStateMachine _stateMachine;
    private readonly PathSelectionStrategy _pathSelector;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _monitorCts;

    // === Connection Properties ===
    [ObservableProperty] private string _roomId = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _connectionStateDisplay = "Idle";
    [ObservableProperty] private string _connectionType = "";
    [ObservableProperty] private string _natType = "";
    [ObservableProperty] private string _ipv4 = "";
    [ObservableProperty] private string _ipv6 = "";
    [ObservableProperty] private string _latency = "";
    [ObservableProperty] private string _lossRate = "";
    [ObservableProperty] private int _peerCount;
    [ObservableProperty] private string _statusText = "未连接";

    // === Advanced Properties ===
    [ObservableProperty] private bool _isSharedNodeEnabled;
    [ObservableProperty] private string _sharedNodeUrls = "";
    [ObservableProperty] private string _logLevel = "Info";
    [ObservableProperty] private bool _isUpnpDisabled = true;
    [ObservableProperty] private string _customStunServers = "";
    [ObservableProperty] private int _maxReconnectAttempts = 5;
    [ObservableProperty] private string _staticVirtualIp = "";

    public MainViewModel(
        EasyTierConfigBuilder configBuilder,
        EasyTierProcessService processService,
        EasyTierCliClient cliClient,
        ConnectionStateMachine stateMachine,
        PathSelectionStrategy pathSelector,
        ILogger<MainViewModel> logger)
    {
        _configBuilder = configBuilder;
        _processService = processService;
        _cliClient = cliClient;
        _stateMachine = stateMachine;
        _pathSelector = pathSelector;
        _logger = logger;
        _stateMachine.StateChanged += OnStateChanged;
    }

    // === Validation ===
    private bool IsRoomIdValid =>
        !string.IsNullOrWhiteSpace(RoomId) && RoomId.Length >= 3 && RoomId.Length <= 64 &&
        !RoomId.Any(char.IsWhiteSpace) && RoomId.All(c => c >= 32 && c <= 126);

    private bool IsPasswordValid => !string.IsNullOrEmpty(Password) && Password.Length <= 128;

    private bool CanConnect =>
        IsRoomIdValid && IsPasswordValid &&
        _stateMachine.CurrentState is Core.ConnectionState.Idle or Core.ConnectionState.Disconnected;

    private bool CanDisconnect =>
        _stateMachine.CurrentState is Core.ConnectionState.Connected or Core.ConnectionState.Monitoring
            or Core.ConnectionState.Connecting or Core.ConnectionState.Reconnecting;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        try
        {
            _stateMachine.UserConnect();
            StatusText = "检测网络中...";
            _logger.LogInformation("Connect: room={Room}", RoomId);

            var room = new RoomOptions { RoomId = RoomId.Trim(), Password = Password };
            var advanced = new AdvancedOptions
            {
                IsSharedNodeEnabled = IsSharedNodeEnabled,
                SharedNodeUrls = SharedNodeUrls,
                LogLevel = LogLevel,
                IsUpnpDisabled = IsUpnpDisabled,
                CustomStunServers = CustomStunServers,
                MaxReconnectAttempts = MaxReconnectAttempts,
                StaticVirtualIp = StaticVirtualIp,
            };

            var config = await _configBuilder.BuildAsync(room, null, advanced);
            _stateMachine.DetectionComplete();
            StatusText = "连接中...";

            try
            {
                await _processService.StartAsync(config.ConfigFilePath, "127.0.0.1:15888", "linkroom");
                _stateMachine.EasyTierReady();
                StatusText = "已连接";
                _monitorCts = new CancellationTokenSource();
                _ = MonitorPeersAsync(_monitorCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EasyTier start failed");
                _stateMachine.DetectionFailed();
                StatusText = $"连接失败: {ex.Message}";
            }
            finally { config.Cleanup(); }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connect failed");
            StatusText = $"错误: {ex.Message}";
            _stateMachine.UserDisconnect();
        }
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        _monitorCts?.Cancel();
        _stateMachine.UserDisconnect();
        await _processService.StopAsync();
        StatusText = "已断开";
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    private void OnStateChanged(object? sender, (ConnectionState Old, ConnectionState New) e)
    {
        ConnectionStateDisplay = e.New.ToString();
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    private async Task MonitorPeersAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(3000, ct);
                var peers = await _cliClient.GetPeersAsync(ct);
                PeerCount = peers.Length;
                if (peers.Length > 0)
                {
                    var p = peers[0];
                    NatType = p.NatType ?? "";
                    Latency = (p.LatencyMs?.ToString("F1") ?? "") + " ms";
                    LossRate = p.LossRate?.ToString("P1") ?? "";
                    Ipv4 = p.IPv4 ?? "";
                    Ipv6 = p.IPv6 ?? "";
                    ConnectionType = p.Cost ?? "";
                    if (_stateMachine.CurrentState == Core.ConnectionState.Connected)
                        _stateMachine.Monitoring();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Peer monitor error");
            }
        }
    }

    partial void OnRoomIdChanged(string value) => ConnectCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => ConnectCommand.NotifyCanExecuteChanged();
}
