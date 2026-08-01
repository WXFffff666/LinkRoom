using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkRoom.Core;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Gui;

/// <summary>
/// ViewModel for the main window: owns every ObservableProperty the XAML
/// binds to and forwards commands to focused services (ConnectFlowService,
/// RoomSessionService, SettingsFacade, UpdateAndToolsService). Pure
/// decomposition — no logic lives here beyond property plumbing.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    public static readonly ObservableCollection<string> LogLines = new() { "[INFO] LinkRoom started" };

    // Services (composition root for the Gui layer).
    readonly ConnectFlowService _cf;
    readonly RoomSessionService _room;
    readonly SettingsFacade _sf;
    readonly UpdateAndToolsService _tools;

    // In-room chat over the virtual network (Core layer, no WPF deps).
    readonly ChatService _chat;

    // Shared infrastructure still owned by the VM (event subscriptions + logging).
    readonly EasyTierProcessService _proc;
    readonly ConnectionStateMachine _sm;
    readonly ILogger<MainViewModel> _log;
    readonly LogBuffer _logBuffer;

    IMainWindowView? _win;
    // Current session's room-lock group_secret (server-side ACL). Set by
    // CreateRoomAsync (host generates fresh) or ConnectAsync (guest reads from
    // the share link). Persisted in AppSettings.RoomLockSecrets[RoomId] so the
    // host re-uses the same secret on reconnects; rotated when the host
    // toggles the lock off and on again.
    string? _lockSecret;

    [ObservableProperty] string _roomId = "", _password = "", _connState = "Idle", _connType = "";
    [ObservableProperty] string _natType = "", _ipv4 = "", _ipv6 = "", _virtualIpv4 = "", _virtualIpv6 = "";
    [ObservableProperty] string _latency = "", _lossRate = "", _connectionQuality = "";
    [ObservableProperty] int _peerCount;
    [ObservableProperty] bool _isRelayMode, _isSharedNodeEnabled;
    [ObservableProperty] string _sharedNodeUrls = AppPaths.DefaultSharedNode, _logLevel = "Info";
    [ObservableProperty] string _customStunServers = "", _staticVirtualIp = "", _passwordStrengthHint = "";
    [ObservableProperty] bool _enableSecureMode = true;
    [ObservableProperty] string _sharedNodePublicKey = "";
    [ObservableProperty] int _maxReconnectAttempts = 5, _listenerPort = 11010, _mtu = 1380;
    [ObservableProperty] bool _portableMode = true, _preferIPv6 = true, _darkMode;
    [ObservableProperty] bool _useLanMode, _isHostMode = true, _autoStart;
    [ObservableProperty] int? _gamePortHint;
    [ObservableProperty] string _portForwardHint = "";
    [ObservableProperty] string _statusText = "就绪", _statusDetail = "创建房间或输入联机码加入";
    [ObservableProperty] string _chatInput = "";
    public ObservableCollection<string> Peers { get; } = new();
    public ObservableCollection<string> RoomHistory { get; } = new();
    public ObservableCollection<string> ChatLines { get; } = new();

    // Shared state exposed to the services (see each service for usage).
    internal IMainWindowView? Window => _win;
    internal string? LockSecret { get => _lockSecret; set => _lockSecret = value; }
    internal AutoReconnectService ReconnectService => _cf.Reconnect;
    internal ChatService Chat => _chat;

    public MainViewModel(
        EasyTierConfigBuilder cfg, EasyTierProcessService proc, EasyTierCliClient cli,
        ConnectionStateMachine sm, PathSelectionStrategy ps, DetectionCache dc,
        NetworkInfoService ns, SettingsService ss,
        PeerPingService ping, WebPanelService web, DiagnosticsService diag,
        NatProbeService natProbe, StunServerProvider stunProvider,
        UpdateService update, ProcessGuardian guardian,
        ILogger<MainViewModel> log, ILogger<AutoReconnectService> reconnectLog)
    {
        _sm = sm; _proc = proc; _log = log;

        _sf = new SettingsFacade(this, ss, ns);
        _cf = new ConnectFlowService(this, cfg, proc, cli, sm, ps, dc, ns, ss, guardian, _sf, reconnectLog);
        _room = new RoomSessionService(this, _cf, proc, sm, guardian);
        _tools = new UpdateAndToolsService(this, update, dc, diag, web, stunProvider, natProbe, ping, _sf, _room);

        // Chat fires on background threads — marshal to the UI thread before
        // touching the observable collection. Chat content intentionally never
        // reaches L()/the log file (privacy).
        _chat = new ChatService();
        _chat.MessageReceived += m => _cf.Ui(() =>
            AddChatLine($"[{m.Ts:HH:mm:ss}] {m.From}: {m.Msg}"));

        // LogBuffer owns the thread-safe, bounded log store; its callback marshals
        // the ObservableCollection write onto the WPF Dispatcher (BUG-2 fix).
        _logBuffer = new LogBuffer(300, line => _cf.Ui(() =>
        {
            LogLines.Add(line);
            while (LogLines.Count > 300) LogLines.RemoveAt(0);
        }));

        _sm.StateChanged += (_, e) =>
        {
            ConnState = e.New.ToString();
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            CreateRoomCommand.NotifyCanExecuteChanged();
        };

        // Process.Exited fires on a thread-pool thread — marshal to the UI thread
        // before touching state/logs (BUG-2 fix).
        _proc.UnexpectedExit += (_, _) => _cf.Ui(() =>
        {
            if (_sm.CurrentState is ConnectionState.Connected or ConnectionState.Monitoring)
            {
                L("连接意外断开，尝试自动重连...");
                _sm.ConnectionLost();
            }
        });
    }

    public void SetWindow(IMainWindowView w) => _win = w;

    public void RestoreSettings(AppSettings s) => _sf.RestoreSettings(s);

    public void SaveSettingsNow() => _sf.SaveSettingsNow();

    bool RoomValid => !string.IsNullOrWhiteSpace(RoomId) && RoomId.Length is >= 3 and <= 64 && !RoomId.Any(char.IsWhiteSpace);
    bool CanConnect => RoomValid && _sm.CurrentState is ConnectionState.Idle or ConnectionState.Disconnected;
    bool CanCreate => _sm.CurrentState is ConnectionState.Idle or ConnectionState.Disconnected;
    bool CanDisconnect => _sm.CurrentState is ConnectionState.Connected or ConnectionState.Monitoring
        or ConnectionState.Connecting or ConnectionState.Reconnecting;

    public void L(string m)
    {
        var line = SettingsService.SanitizeLog($"[{DateTime.Now:HH:mm:ss}] {m}");
        _logBuffer.Add(line); // thread-safe add; buffer callback marshals LogLines onto the UI thread
        _log.LogInformation(m);
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    Task CreateRoomAsync() => _room.CreateRoomAsync();

    [RelayCommand(CanExecute = nameof(CanConnect))]
    Task ConnectAsync() => _room.ConnectAsync();

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    Task DisconnectAsync() => _room.DisconnectAsync();

    [RelayCommand]
    Task PingPeersAsync() => _tools.PingPeersAsync();

    [RelayCommand]
    Task ExportDiagnosticsAsync() => _tools.ExportDiagnosticsAsync();

    [RelayCommand]
    void OpenWebPanel() => _tools.OpenWebPanel();

    [RelayCommand]
    void CopyLinkCode() => _tools.CopyLinkCode();

    [RelayCommand]
    Task RefreshStunListAsync() => _tools.RefreshStunListAsync();

    [RelayCommand]
    Task JoinHistoryRoomAsync(string? room) => _room.JoinHistoryRoomAsync(room);

    [RelayCommand]
    Task CheckUpdateManualAsync() => _tools.CheckUpdateManualAsync();

    [RelayCommand]
    void SendChat()
    {
        var text = ChatInput.Trim();
        if (text.Length == 0) return;
        ChatInput = "";

        // Chat content stays in the chat pane — never L()/log file.
        if (!_chat.IsRunning)
        {
            AddChatLine($"[{DateTime.Now:HH:mm:ss}] 系统: 未连接房间");
            return;
        }

        var error = _chat.SendMessage(Nickname, text);
        if (error != null)
            AddChatLine($"[{DateTime.Now:HH:mm:ss}] 系统: {error}");
    }

    // Nickname defaults to the machine name, falling back to the virtual IP.
    string Nickname => string.IsNullOrWhiteSpace(Environment.MachineName)
        ? VirtualIpv4
        : Environment.MachineName;

    void AddChatLine(string line)
    {
        ChatLines.Add(line);
        while (ChatLines.Count > 100) ChatLines.RemoveAt(0);
    }

    [RelayCommand]
    void CopyVirtualIp() => _tools.CopyVirtualIp();

    [RelayCommand]
    Task SpeedTestAsync() => _tools.SpeedTestAsync();

    [RelayCommand]
    Task ExportConfigAsync() => _tools.ExportConfigAsync();

    [RelayCommand]
    Task ImportConfigAsync() => _tools.ImportConfigAsync();

    [RelayCommand]
    void ScanMods() => _tools.ScanMods();

    [RelayCommand]
    Task CheckEasyTierVersionAsync() => _tools.CheckEasyTierVersionAsync();

    [RelayCommand]
    void RefreshNetwork() => _tools.RefreshNetwork();

    public Task CheckUpdateOnStartupAsync() => _tools.CheckUpdateOnStartupAsync();

    public Task RunNatTestAsync(Action<string> report) => _tools.RunNatTestAsync(report);

    public string RunSelfCheck() => _tools.RunSelfCheck();

    partial void OnRoomIdChanged(string value) => ConnectCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        PasswordStrengthHint = PasswordStrength.Hint(PasswordStrength.Evaluate(value));
    }
    partial void OnPortableModeChanged(bool value) { AppPaths.Configure(value); SaveSettingsNow(); }
    partial void OnAutoStartChanged(bool value) => AutoStartService.SetEnabled(value);
    partial void OnMaxReconnectAttemptsChanged(int value) { _cf.Reconnect.MaxAttempts = value > 0 ? value : 5; SaveSettingsNow(); }
}
