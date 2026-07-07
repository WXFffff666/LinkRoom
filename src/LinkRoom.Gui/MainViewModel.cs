using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkRoom.Core;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Gui;

public partial class MainViewModel : ObservableObject
{
    private readonly EasyTierConfigBuilder _configBuilder;
    private readonly EasyTierProcessService _processService;
    private readonly EasyTierCliClient _cliClient;
    private readonly ConnectionStateMachine _stateMachine;
    private readonly PathSelectionStrategy _pathSelector;
    private readonly DetectionCache _detectionCache;
    private readonly NetworkInfoService _networkService;
    private readonly SettingsService _settings;
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
    [ObservableProperty] private string _statusDetail = "";

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
        DetectionCache detectionCache,
        NetworkInfoService networkService,
        SettingsService settings,
        ILogger<MainViewModel> logger)
    {
        _configBuilder = configBuilder;
        _processService = processService;
        _cliClient = cliClient;
        _stateMachine = stateMachine;
        _pathSelector = pathSelector;
        _detectionCache = detectionCache;
        _networkService = networkService;
        _settings = settings;
        _logger = logger;
        _stateMachine.StateChanged += OnStateChanged;
    }

    public void RestoreSettings(AppSettings s)
    {
        if (!string.IsNullOrEmpty(s.LastRoomId)) RoomId = s.LastRoomId;
        IsSharedNodeEnabled = s.IsSharedNodeEnabled;
        if (!string.IsNullOrEmpty(s.SharedNodeUrls)) SharedNodeUrls = s.SharedNodeUrls;
        LogLevel = s.LogLevel ?? "Info";
        IsUpnpDisabled = s.IsUpnpDisabled;
        if (!string.IsNullOrEmpty(s.CustomStunServers)) CustomStunServers = s.CustomStunServers;
        MaxReconnectAttempts = s.MaxReconnectAttempts > 0 ? s.MaxReconnectAttempts : 5;
        if (!string.IsNullOrEmpty(s.StaticVirtualIp)) StaticVirtualIp = s.StaticVirtualIp;
    }

    private void SetStatus(string text, string detail)
    {
        StatusText = text;
        StatusDetail = detail;
    }

    // === Validation ===
    private bool IsRoomIdValid => !string.IsNullOrWhiteSpace(RoomId) && RoomId.Length >= 3 && RoomId.Length <= 64 && !RoomId.Any(char.IsWhiteSpace) && RoomId.All(c => c >= 32 && c <= 126);
    private bool IsPasswordValid => !string.IsNullOrEmpty(Password) && Password.Length <= 128;
    private bool CanConnect => IsRoomIdValid && IsPasswordValid && _stateMachine.CurrentState is Core.ConnectionState.Idle or Core.ConnectionState.Disconnected;
    private bool CanDisconnect => _stateMachine.CurrentState is Core.ConnectionState.Connected or Core.ConnectionState.Monitoring or Core.ConnectionState.Connecting or Core.ConnectionState.Reconnecting;

    private AdvancedOptions GetAdvanced() => new()
    {
        IsSharedNodeEnabled = IsSharedNodeEnabled, SharedNodeUrls = SharedNodeUrls,
        LogLevel = LogLevel, IsUpnpDisabled = IsUpnpDisabled,
        CustomStunServers = CustomStunServers, MaxReconnectAttempts = MaxReconnectAttempts,
        StaticVirtualIp = StaticVirtualIp,
    };

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        var room = new RoomOptions { RoomId = RoomId.Trim(), Password = Password };
        var advanced = GetAdvanced();

        try
        {
            _stateMachine.UserConnect();
            StatusText = "检测网络中...";
            SetStatus("检测网络中...", "正在探测 NAT 类型...");

            // === PHASE 1: Network Detection ===
            NetworkSnapshot? snapshot = null;
            try
            {
                snapshot = await _detectionCache.GetAsync();
                NatType = snapshot.NatType.ToString();
                Ipv4 = snapshot.PublicIPv4 ?? "";
                Ipv6 = snapshot.PublicIPv6 ?? "";
                _logger.LogInformation("Detection complete: {Nat}, IPv4={IPv4}", snapshot.NatType, snapshot.PublicIPv4);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Network detection failed — proceeding without detection");
                StatusText = "检测失败，跳过...";
            }

            _stateMachine.DetectionComplete();

            // === PHASE 2: Path Selection ===
            var path = _pathSelector.Evaluate(snapshot, advanced);
            foreach (var w in path.Warnings)
            {
                _logger.LogWarning("Path warning: {Warning}", w);
                SetStatus("连接中...", w);
            }

            // === PHASE 3: Build Config ===
            StatusText = "生成配置中...";
            var config = await _configBuilder.BuildAsync(room, snapshot, advanced);

            // === PHASE 4: Start EasyTier Core ===
            StatusText = "连接中...";
            SetStatus("连接中...", "正在启动 EasyTier 核心...");

            try
            {
                await _processService.StartAsync(config.ConfigFilePath, "127.0.0.1:15888", "linkroom");
                _stateMachine.EasyTierReady();
                StatusText = "已连接";
                ConnectionStateDisplay = "Connected";
                SetStatus("已连接", $"NAT: {snapshot?.NatType}, 路径: {path.Strategy}");

                // Save last room ID
                _settings.Save(new AppSettings
                {
                    LastRoomId = RoomId.Trim(),
                    IsSharedNodeEnabled = IsSharedNodeEnabled,
                    SharedNodeUrls = SharedNodeUrls,
                    LogLevel = LogLevel,
                    IsUpnpDisabled = IsUpnpDisabled,
                    CustomStunServers = CustomStunServers,
                    MaxReconnectAttempts = MaxReconnectAttempts,
                    StaticVirtualIp = StaticVirtualIp,
                });

                // === PHASE 5: Monitor Peers ===
                _monitorCts = new CancellationTokenSource();
                _ = MonitorPeersAsync(_monitorCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EasyTier start failed");
                _stateMachine.DetectionFailed();
                StatusText = $"连接失败: {ex.Message}";
                SetStatus("连接失败", ex.Message);
            }
            finally { config.Cleanup(); }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connect failed");
            StatusText = $"错误: {ex.Message}";
            SetStatus("错误", ex.Message);
            _stateMachine.UserDisconnect();
        }
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        await (_monitorCts?.CancelAsync() ?? Task.CompletedTask);
        _stateMachine.UserDisconnect();
        await _processService.StopAsync();
        StatusText = "已断开";
        ConnectionStateDisplay = "Disconnected";
        SetStatus("已断开", "");
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    private void OnStateChanged(object? s, (ConnectionState Old, ConnectionState New) e)
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
            catch (Exception ex) { _logger.LogDebug(ex, "Peer monitor error"); }
        }
    }

    partial void OnRoomIdChanged(string value) => ConnectCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => ConnectCommand.NotifyCanExecuteChanged();
}