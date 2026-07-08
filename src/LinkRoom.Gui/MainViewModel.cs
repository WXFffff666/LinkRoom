using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkRoom.Core;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Gui;

public partial class MainViewModel : ObservableObject
{
    public static readonly ObservableCollection<string> LogLines = new() { "[INFO] LinkRoom started" };

    readonly EasyTierConfigBuilder _cfg; readonly EasyTierProcessService _proc; readonly EasyTierCliClient _cli;
    readonly ConnectionStateMachine _sm; readonly PathSelectionStrategy _ps;
    readonly DetectionCache _dc; readonly NetworkInfoService _ns; readonly SettingsService _ss;
    readonly ILogger<MainViewModel> _log;
    CancellationTokenSource? _mon; IMainWindowView? _win; EasyTierLaunchConfig? _acfg;

    [ObservableProperty] string _roomId = "", _password = "", _connState = "Idle", _connType = "";
    [ObservableProperty] string _natType = "", _ipv4 = "", _ipv6 = "", _latency = "", _lossRate = "";
    [ObservableProperty] int _peerCount;
    public ObservableCollection<string> Peers { get; } = new();
    [ObservableProperty] string _statusText = "\u5c31\u7eea", _statusDetail = "\u8f93\u5165\u623f\u95f4\u53f7\u6216\u70b9\u51fb\u521b\u5efa\u623f\u95f4";
    [ObservableProperty] bool _isSharedNodeEnabled, _isUpnpDisabled = true;
    [ObservableProperty] string _sharedNodeUrls = "", _logLevel = "Info", _customStunServers = "", _staticVirtualIp = "";
    [ObservableProperty] int _maxReconnectAttempts = 5, _listenerPort = 11010, _mtu = 1380;
    [ObservableProperty] bool _portableMode, _preferIPv6, _darkMode;

    public MainViewModel(EasyTierConfigBuilder cfg, EasyTierProcessService proc, EasyTierCliClient cli,
        ConnectionStateMachine sm, PathSelectionStrategy ps, DetectionCache dc,
        NetworkInfoService ns, SettingsService ss, ILogger<MainViewModel> log)
    {
        _cfg = cfg; _proc = proc; _cli = cli; _sm = sm; _ps = ps; _dc = dc; _ns = ns; _ss = ss; _log = log;
        _sm.StateChanged += (_, e) => { ConnState = e.New.ToString(); ConnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged(); };
    }

    public void SetWindow(IMainWindowView w) => _win = w;

    public void RestoreSettings(AppSettings s)
    {
        if (!string.IsNullOrEmpty(s.LastRoomId)) RoomId = s.LastRoomId;
        IsSharedNodeEnabled = s.IsSharedNodeEnabled; SharedNodeUrls = s.SharedNodeUrls ?? "";
        LogLevel = s.LogLevel ?? "Info"; IsUpnpDisabled = s.IsUpnpDisabled;
        CustomStunServers = s.CustomStunServers ?? ""; StaticVirtualIp = s.StaticVirtualIp ?? "";
        MaxReconnectAttempts = s.MaxReconnectAttempts > 0 ? s.MaxReconnectAttempts : 5;
        ListenerPort = s.ListenerPort > 0 ? s.ListenerPort : 11010;
        Mtu = s.Mtu is >= 576 and <= 1500 ? s.Mtu : 1380;
        PreferIPv6 = s.PreferIPv6; PortableMode = s.PortableMode;
        DarkMode = s.DarkMode;
    }

    bool RoomValid => !string.IsNullOrWhiteSpace(RoomId) && RoomId.Length is >= 3 and <= 64 && !RoomId.Any(char.IsWhiteSpace);
    bool PwValid => Password.Length <= 128;
    bool CanConnect => RoomValid && _sm.CurrentState is ConnectionState.Idle or ConnectionState.Disconnected;
    bool CanCreate => _sm.CurrentState is ConnectionState.Idle or ConnectionState.Disconnected;
    bool CanDisconnect => _sm.CurrentState is ConnectionState.Connected or ConnectionState.Monitoring or ConnectionState.Connecting or ConnectionState.Reconnecting;

    AdvancedOptions Adv() => new()
    {
        IsSharedNodeEnabled = IsSharedNodeEnabled, SharedNodeUrls = SharedNodeUrls, LogLevel = LogLevel,
        IsUpnpDisabled = IsUpnpDisabled, CustomStunServers = CustomStunServers,
        MaxReconnectAttempts = MaxReconnectAttempts, StaticVirtualIp = StaticVirtualIp, ListenerPort = ListenerPort,
        Mtu = Mtu, PreferIPv6 = PreferIPv6, PortableMode = PortableMode,
    };

    void L(string m) { LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {m}"); _log.LogInformation(m); }

    static string GenId() { var b = RandomNumberGenerator.GetBytes(8); var sb = new StringBuilder(8); const string c = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; for (int i = 0; i < 8; i++) sb.Append(c[b[i] % c.Length]); return sb.ToString(); }
    static string GenPw() { var b = RandomNumberGenerator.GetBytes(6); var sb = new StringBuilder(6); const string c = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789"; for (int i = 0; i < 6; i++) sb.Append(c[b[i] % c.Length]); return sb.ToString(); }

    async Task<NetworkSnapshot?> DetectAsync()
    { try { var s = await _dc.GetAsync(); NatType = s.NatType.ToString(); Ipv4 = s.PublicIPv4 ?? ""; return s; } catch { L("NAT detection failed"); return null; } }

    AppSettings Save() => new()
    {
        LastRoomId = RoomId.Trim(), IsSharedNodeEnabled = IsSharedNodeEnabled, SharedNodeUrls = SharedNodeUrls,
        LogLevel = LogLevel, IsUpnpDisabled = IsUpnpDisabled, CustomStunServers = CustomStunServers,
        MaxReconnectAttempts = MaxReconnectAttempts, StaticVirtualIp = StaticVirtualIp, ListenerPort = ListenerPort,
        Mtu = Mtu, PreferIPv6 = PreferIPv6, PortableMode = PortableMode, DarkMode = DarkMode,
    };

    [RelayCommand(CanExecute = nameof(CanCreate))]
    async Task CreateRoomAsync()
    {
        try {
            var id = GenId(); var pw = _win?.GetCreatePassword() ?? ""; if (string.IsNullOrEmpty(pw)) pw = GenPw();
            RoomId = id; Password = pw;
            L($"Creating room: {id}"); _win?.ShowCreatedRoom(id);
            await ConnectInternalAsync(new RoomOptions { RoomId = id, Password = pw });
        } catch (Exception ex) { L($"Create room error: {ex.Message}"); StatusText = "创建失败"; StatusDetail = ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    async Task ConnectAsync()
    {
        try {
            L($"Joining room: {RoomId.Trim()}");
            await ConnectInternalAsync(new RoomOptions { RoomId = RoomId.Trim(), Password = Password });
        } catch (Exception ex) { L($"Join room error: {ex.Message}"); StatusText = "加入失败"; StatusDetail = ex.Message; }
    }

    async Task ConnectInternalAsync(RoomOptions room)
    {
        var adv = Adv(); _sm.UserConnect(); StatusText = "connecting...";
        try
        {
            var snap = await DetectAsync(); _sm.DetectionComplete();
            var path = _ps.Evaluate(snap, adv);
            _acfg = await _cfg.BuildAsync(room, snap, adv);
            await _proc.StartAsync(_acfg.ConfigFilePath, "127.0.0.1:15888", "linkroom");
            _sm.EasyTierReady(); StatusText = "connected";
            StatusDetail = $"NAT:{snap?.NatType} path:{path.Strategy} port:{adv.ListenerPort}";
            ConnState = "Connected"; _ss.Save(Save());
            _mon = new CancellationTokenSource(); _ = MonitorAsync(_mon.Token);
            L($"Connected: room={room.RoomId} nat={snap?.NatType} path={path.Strategy}");
        }
        catch (Exception ex) { L($"Error: {ex.Message}"); StatusText = "failed"; StatusDetail = ex.Message; _sm.UserDisconnect(); }
        finally { _acfg?.Cleanup(); ConnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged(); }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    async Task DisconnectAsync()
    {
        await (_mon?.CancelAsync() ?? Task.CompletedTask); _sm.UserDisconnect();
        await _proc.StopAsync(); StatusText = "disconnected"; StatusDetail = "";
        ConnState = "Disconnected"; L("Disconnected");
        ConnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    async Task EndRoomAsync()
    {
        L("Ending room — notifying peers...");
        await DisconnectAsync();
        StatusText = "room ended"; StatusDetail = "房间已结束，所有连接已断开";
    }

    async Task MonitorAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(3000, ct); var ps = await _cli.GetPeersAsync(ct); PeerCount = ps.Length;
                Application.Current?.Dispatcher.InvokeAsync(() => { Peers.Clear(); foreach (var p in ps) Peers.Add($"{p.IPv4 ?? "?"} | NAT:{p.NatType ?? "?"} | {p.LatencyMs?.ToString("F0") ?? "?"}ms | {p.Cost ?? "?"}"); });
                if (ps.Length > 0) { var p = ps[0]; NatType = p.NatType ?? ""; Latency = (p.LatencyMs?.ToString("F1") ?? "") + "ms"; LossRate = p.LossRate?.ToString("P1") ?? ""; ConnType = p.Cost ?? ""; if (_sm.CurrentState == ConnectionState.Connected) _sm.Monitoring(); }
            }
            catch (OperationCanceledException) { break; } catch { }
        }
    }

    partial void OnRoomIdChanged(string value) => ConnectCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => ConnectCommand.NotifyCanExecuteChanged();
}