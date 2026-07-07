using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkRoom.Core;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Gui;

public partial class MainViewModel : ObservableObject
{
    public static readonly ObservableCollection<string> LogLines = new() { "LinkRoom 启动" };
    public string LogText => string.Join(Environment.NewLine, LogLines);

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
    private IMainWindowView? _window;
    private EasyTierLaunchConfig? _activeConfig;

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
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _statusDetail = "选择「创建房间」或「加入房间」开始";

    // === Advanced Properties ===
    [ObservableProperty] private bool _isSharedNodeEnabled;
    [ObservableProperty] private string _sharedNodeUrls = "";
    [ObservableProperty] private string _logLevel = "Info";
    [ObservableProperty] private bool _isUpnpDisabled = true;
    [ObservableProperty] private string _customStunServers = "";
    [ObservableProperty] private int _maxReconnectAttempts = 5;
    [ObservableProperty] private string _staticVirtualIp = "";

    public MainViewModel(
        EasyTierConfigBuilder configBuilder, EasyTierProcessService processService,
        EasyTierCliClient cliClient, ConnectionStateMachine stateMachine,
        PathSelectionStrategy pathSelector, DetectionCache detectionCache,
        NetworkInfoService networkService, SettingsService settings,
        ILogger<MainViewModel> logger)
    {
        _configBuilder = configBuilder; _processService = processService;
        _cliClient = cliClient; _stateMachine = stateMachine;
        _pathSelector = pathSelector; _detectionCache = detectionCache;
        _networkService = networkService; _settings = settings;
        _logger = logger;
        _stateMachine.StateChanged += OnStateChanged;
    }

    public void SetWindow(IMainWindowView w) { _window = w; }
    public void RestoreSettings(AppSettings s)
    {
        if (!string.IsNullOrEmpty(s.LastRoomId)) RoomId = s.LastRoomId;
        IsSharedNodeEnabled = s.IsSharedNodeEnabled; SharedNodeUrls = s.SharedNodeUrls ?? "";
        LogLevel = s.LogLevel ?? "Info"; IsUpnpDisabled = s.IsUpnpDisabled;
        CustomStunServers = s.CustomStunServers ?? "";
        MaxReconnectAttempts = s.MaxReconnectAttempts > 0 ? s.MaxReconnectAttempts : 5;
        StaticVirtualIp = s.StaticVirtualIp ?? "";
    }

    private bool IsRoomIdValid => !string.IsNullOrWhiteSpace(RoomId) && RoomId.Length >= 3 && RoomId.Length <= 64 && !RoomId.Any(char.IsWhiteSpace) && RoomId.All(c => c >= 32 && c <= 126);
    private bool IsPasswordValid => Password.Length <= 128;
    private bool CanConnect => IsRoomIdValid && IsPasswordValid && _stateMachine.CurrentState is Core.ConnectionState.Idle or Core.ConnectionState.Disconnected;
    private bool CanDisconnect => _stateMachine.CurrentState is Core.ConnectionState.Connected or Core.ConnectionState.Monitoring or Core.ConnectionState.Connecting or Core.ConnectionState.Reconnecting;
    private bool CanCreateRoom => _stateMachine.CurrentState is Core.ConnectionState.Idle or Core.ConnectionState.Disconnected;

    private AdvancedOptions GetAdvanced() => new()
    {
        IsSharedNodeEnabled = IsSharedNodeEnabled, SharedNodeUrls = SharedNodeUrls,
        LogLevel = LogLevel, IsUpnpDisabled = IsUpnpDisabled,
        CustomStunServers = CustomStunServers, MaxReconnectAttempts = MaxReconnectAttempts,
        StaticVirtualIp = StaticVirtualIp,
    };

    /// <summary>Generates a random room ID (12 alphanumeric chars).</summary>
    private static string GenerateRoomId()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = RandomNumberGenerator.GetBytes(12);
        var sb = new StringBuilder(12);
        for (int i = 0; i < 12; i++) sb.Append(chars[bytes[i] % chars.Length]);
        return sb.ToString();
    }

    // === CREATE ROOM ===
    [RelayCommand(CanExecute = nameof(CanCreateRoom))]
    private async Task CreateRoomAsync()
    {
        var roomId = GenerateRoomId();
        var createPass = _window?.GetCreatePassword() ?? "";
        RoomId = roomId;
        Password = createPass;

            _logger.LogInformation("Creating room: {Room}", roomId);
            LogLines.Add($"[INFO] 创建房间: {roomId}");
            _window?.ShowCreatedRoom(roomId);

        var room = new RoomOptions { RoomId = roomId, Password = createPass };
        var advanced = GetAdvanced();

        try
        {
            _stateMachine.UserConnect();
            StatusText = "创建房间中...";
            StatusDetail = "正在探测网络...";

            NetworkSnapshot? snapshot = null;
            try { snapshot = await _detectionCache.GetAsync(); NatType = snapshot.NatType.ToString(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Detection failed"); }

            _stateMachine.DetectionComplete();
            var path = _pathSelector.Evaluate(snapshot, advanced);

            _activeConfig = await _configBuilder.BuildAsync(room, snapshot, advanced);
            await _processService.StartAsync(_activeConfig.ConfigFilePath, "127.0.0.1:15888", "linkroom");
            _stateMachine.EasyTierReady();
            StatusText = "房间已创建";
            StatusDetail = $"房间号: {roomId} · NAT: {snapshot?.NatType}";
            ConnectionStateDisplay = "Connected";

            _settings.Save(new AppSettings { LastRoomId = roomId, IsSharedNodeEnabled = IsSharedNodeEnabled, SharedNodeUrls = SharedNodeUrls, LogLevel = LogLevel, IsUpnpDisabled = IsUpnpDisabled, MaxReconnectAttempts = MaxReconnectAttempts, StaticVirtualIp = StaticVirtualIp });

            _monitorCts = new CancellationTokenSource();
            _ = MonitorPeersAsync(_monitorCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create room failed");
            StatusText = "创建失败"; StatusDetail = ex.Message;
            _stateMachine.UserDisconnect();
        }
        finally { _activeConfig?.Cleanup(); }
        ConnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged();
    }

    // === JOIN ROOM (Connect) ===
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        var room = new RoomOptions { RoomId = RoomId.Trim(), Password = Password };
        var advanced = GetAdvanced();
        try
        {
            _stateMachine.UserConnect();
            StatusText = "检测网络中..."; StatusDetail = "正在探测 NAT 类型...";
            NetworkSnapshot? snapshot = null;
            try { snapshot = await _detectionCache.GetAsync(); NatType = snapshot.NatType.ToString(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Detection failed"); }
            _stateMachine.DetectionComplete();

            var path = _pathSelector.Evaluate(snapshot, advanced);
            _activeConfig = await _configBuilder.BuildAsync(room, snapshot, advanced);

            StatusText = "连接中..."; StatusDetail = "正在启动 EasyTier 核心...";
            await _processService.StartAsync(_activeConfig.ConfigFilePath, "127.0.0.1:15888", "linkroom");
            _stateMachine.EasyTierReady();
            StatusText = "已连接"; StatusDetail = $"NAT: {snapshot?.NatType}, 路径: {path.Strategy}";
            ConnectionStateDisplay = "Connected";

            _settings.Save(new AppSettings { LastRoomId = RoomId.Trim(), IsSharedNodeEnabled = IsSharedNodeEnabled, SharedNodeUrls = SharedNodeUrls, LogLevel = LogLevel, IsUpnpDisabled = IsUpnpDisabled, MaxReconnectAttempts = MaxReconnectAttempts, StaticVirtualIp = StaticVirtualIp });
            _monitorCts = new CancellationTokenSource();
            _ = MonitorPeersAsync(_monitorCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connect failed");
            StatusText = "连接失败"; StatusDetail = ex.Message;
            _stateMachine.UserDisconnect();
        }
        finally { _activeConfig?.Cleanup(); }
        ConnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        await (_monitorCts?.CancelAsync() ?? Task.CompletedTask);
        _stateMachine.UserDisconnect();
        await _processService.StopAsync();
        StatusText = "已断开"; StatusDetail = "";
        ConnectionStateDisplay = "Disconnected";
        ConnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged();
    }

    private void OnStateChanged(object? s, (ConnectionState Old, ConnectionState New) e)
    {
        ConnectionStateDisplay = e.New.ToString();
        ConnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged();
        CreateRoomCommand.NotifyCanExecuteChanged();
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
                if (peers.Length > 0) { var p = peers[0]; NatType = p.NatType ?? ""; Latency = (p.LatencyMs?.ToString("F1") ?? "") + " ms"; LossRate = p.LossRate?.ToString("P1") ?? ""; Ipv4 = p.IPv4 ?? ""; Ipv6 = p.IPv6 ?? ""; ConnectionType = p.Cost ?? ""; if (_stateMachine.CurrentState == Core.ConnectionState.Connected) _stateMachine.Monitoring(); }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    partial void OnRoomIdChanged(string value) => ConnectCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => ConnectCommand.NotifyCanExecuteChanged();
}