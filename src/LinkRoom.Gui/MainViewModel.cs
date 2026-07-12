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

    readonly EasyTierConfigBuilder _cfg;
    readonly EasyTierProcessService _proc;
    readonly EasyTierCliClient _cli;
    readonly ConnectionStateMachine _sm;
    readonly PathSelectionStrategy _ps;
    readonly DetectionCache _dc;
    readonly NetworkInfoService _ns;
    readonly SettingsService _ss;
    readonly AutoReconnectService _reconnect;
    readonly PeerPingService _ping;
    readonly WebPanelService _web;
    readonly DiagnosticsService _diag;
    readonly NatProbeService _natProbe;
    readonly StunServerProvider _stunProvider;
    readonly ILogger<MainViewModel> _log;

    CancellationTokenSource? _mon;
    IMainWindowView? _win;
    EasyTierLaunchConfig? _acfg;
    RoomOptions? _lastRoom;

    [ObservableProperty] string _roomId = "", _password = "", _connState = "Idle", _connType = "";
    [ObservableProperty] string _natType = "", _ipv4 = "", _ipv6 = "", _virtualIpv4 = "", _virtualIpv6 = "";
    [ObservableProperty] string _latency = "", _lossRate = "", _connectionQuality = "";
    [ObservableProperty] int _peerCount;
    [ObservableProperty] bool _isRelayMode, _isSharedNodeEnabled;
    [ObservableProperty] string _sharedNodeUrls = AppPaths.DefaultSharedNode, _logLevel = "Info";
    [ObservableProperty] string _customStunServers = "", _staticVirtualIp = "", _passwordStrengthHint = "";
    [ObservableProperty] int _maxReconnectAttempts = 5, _listenerPort = 11010, _mtu = 1380;
    [ObservableProperty] bool _portableMode = true, _preferIPv6 = true, _darkMode;
    [ObservableProperty] bool _useLanMode, _isHostMode = true, _autoStart;
    [ObservableProperty] int? _gamePortHint;
    [ObservableProperty] string _portForwardHint = "";
    [ObservableProperty] string _statusText = "就绪", _statusDetail = "创建房间或输入联机码加入";
    public ObservableCollection<string> Peers { get; } = new();
    public ObservableCollection<string> RoomHistory { get; } = new();

    public MainViewModel(
        EasyTierConfigBuilder cfg, EasyTierProcessService proc, EasyTierCliClient cli,
        ConnectionStateMachine sm, PathSelectionStrategy ps, DetectionCache dc,
        NetworkInfoService ns, SettingsService ss,
        PeerPingService ping, WebPanelService web, DiagnosticsService diag,
        NatProbeService natProbe, StunServerProvider stunProvider,
        ILogger<MainViewModel> log, ILogger<AutoReconnectService> reconnectLog)
    {
        _cfg = cfg; _proc = proc; _cli = cli; _sm = sm; _ps = ps; _dc = dc;
        _ns = ns; _ss = ss; _ping = ping; _web = web;
        _diag = diag; _natProbe = natProbe; _stunProvider = stunProvider; _log = log;

        _reconnect = new AutoReconnectService(sm, ReconnectAsync, reconnectLog);

        _sm.StateChanged += (_, e) =>
        {
            ConnState = e.New.ToString();
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            CreateRoomCommand.NotifyCanExecuteChanged();
        };

        _proc.UnexpectedExit += (_, _) =>
        {
            if (_sm.CurrentState is ConnectionState.Connected or ConnectionState.Monitoring)
            {
                L("连接意外断开，尝试自动重连...");
                _sm.ConnectionLost();
            }
        };
    }

    async Task ReconnectAsync(CancellationToken ct)
    {
        if (_lastRoom == null) return;
        await ConnectInternalAsync(_lastRoom, isReconnect: true);
    }

    public void SetWindow(IMainWindowView w) => _win = w;

    public void RestoreSettings(AppSettings s)
    {
        AppPaths.Configure(s.PortableMode);
        if (!string.IsNullOrEmpty(s.LastRoomId)) RoomId = s.LastRoomId;
        IsSharedNodeEnabled = s.IsSharedNodeEnabled;
        SharedNodeUrls = string.IsNullOrWhiteSpace(s.SharedNodeUrls) ? AppPaths.DefaultSharedNode : s.SharedNodeUrls;
        LogLevel = s.LogLevel ?? "Info";
        CustomStunServers = s.CustomStunServers ?? "";
        StaticVirtualIp = s.StaticVirtualIp ?? "";
        MaxReconnectAttempts = s.MaxReconnectAttempts > 0 ? s.MaxReconnectAttempts : 5;
        ListenerPort = s.ListenerPort > 0 ? s.ListenerPort : 11010;
        Mtu = s.Mtu is >= 576 and <= 1500 ? s.Mtu : 1380;
        PreferIPv6 = s.PreferIPv6;
        PortableMode = s.PortableMode;
        DarkMode = s.DarkMode;
        UseLanMode = s.UseLanMode;
        IsHostMode = s.IsHostMode;
        AutoStart = s.AutoStart;
        GamePortHint = s.GamePortHint;
        _reconnect.MaxAttempts = MaxReconnectAttempts;
        _ns.SetCustomStunServers(CustomStunServers);

        RoomHistory.Clear();
        foreach (var r in s.RoomHistory ?? []) RoomHistory.Add(r);

        if (AutoStart != AutoStartService.IsEnabled())
            AutoStartService.SetEnabled(AutoStart);
    }

    bool RoomValid => !string.IsNullOrWhiteSpace(RoomId) && RoomId.Length is >= 3 and <= 64 && !RoomId.Any(char.IsWhiteSpace);
    bool CanConnect => RoomValid && _sm.CurrentState is ConnectionState.Idle or ConnectionState.Disconnected;
    bool CanCreate => _sm.CurrentState is ConnectionState.Idle or ConnectionState.Disconnected;
    bool CanDisconnect => _sm.CurrentState is ConnectionState.Connected or ConnectionState.Monitoring
        or ConnectionState.Connecting or ConnectionState.Reconnecting;

    AdvancedOptions Adv() => new()
    {
        IsSharedNodeEnabled = IsSharedNodeEnabled,
        SharedNodeUrls = SharedNodeUrls,
        LogLevel = LogLevel,
        IsUpnpDisabled = true,
        CustomStunServers = CustomStunServers,
        MaxReconnectAttempts = MaxReconnectAttempts,
        StaticVirtualIp = StaticVirtualIp,
        ListenerPort = ListenerPort,
        Mtu = Mtu,
        PreferIPv6 = PreferIPv6,
        PortableMode = PortableMode,
        UseLanMode = UseLanMode,
        IsHostMode = IsHostMode,
        GamePortHint = GamePortHint,
    };

    void L(string m)
    {
        var line = SettingsService.SanitizeLog($"[{DateTime.Now:HH:mm:ss}] {m}");
        LogLines.Add(line);
        _log.LogInformation(m);
    }

    static string GenId()
    {
        var b = RandomNumberGenerator.GetBytes(8);
        var sb = new StringBuilder(8);
        const string c = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int i = 0; i < 8; i++) sb.Append(c[b[i] % c.Length]);
        return sb.ToString();
    }

    static string GenPw()
    {
        var b = RandomNumberGenerator.GetBytes(8);
        var sb = new StringBuilder(8);
        const string c = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        for (int i = 0; i < 8; i++) sb.Append(c[b[i] % c.Length]);
        return sb.ToString();
    }

    AppSettings SaveSettings() => new()
    {
        LastRoomId = RoomId.Trim(),
        RoomHistory = RoomHistory.ToList(),
        IsSharedNodeEnabled = IsSharedNodeEnabled,
        SharedNodeUrls = SharedNodeUrls,
        LogLevel = LogLevel,
        CustomStunServers = CustomStunServers,
        MaxReconnectAttempts = MaxReconnectAttempts,
        StaticVirtualIp = StaticVirtualIp,
        ListenerPort = ListenerPort,
        Mtu = Mtu,
        PreferIPv6 = PreferIPv6,
        PortableMode = PortableMode,
        DarkMode = DarkMode,
        UseLanMode = UseLanMode,
        IsHostMode = IsHostMode,
        AutoStart = AutoStart,
        GamePortHint = GamePortHint,
    };

    async Task ConnectInternalAsync(RoomOptions room, bool isReconnect = false)
    {
        if (UseLanMode && !AdminHelper.IsAdministrator())
        {
            StatusText = "需要管理员权限";
            StatusDetail = "LAN 模式需要管理员权限，请右键以管理员运行";
            return;
        }

        var adv = Adv();
        _lastRoom = room;
        _reconnect.MaxAttempts = adv.MaxReconnectAttempts;
        _ns.SetCustomStunServers(adv.CustomStunServers);
        if (!isReconnect) _dc.Invalidate();

        if (!isReconnect) _sm.UserConnect();
        StatusText = isReconnect ? "重连中..." : "连接中...";
        StatusDetail = "正在检测 NAT 类型...";

        try
        {
            if (isReconnect)
            {
                await _proc.StopAsync();
                EasyTierProcessService.KillOrphanProcesses();
            }

            var snap = await _dc.GetAsync();
            NatType = snap.NatType.ToString();
            Ipv4 = snap.PublicIPv4 ?? "";
            Ipv6 = snap.PublicIPv6 ?? "";

            if (!isReconnect) _sm.DetectionComplete();

            var path = _ps.Evaluate(snap, adv);
            foreach (var w in path.Warnings) L($"⚠ {w}");

            _acfg = await _cfg.BuildAsync(room, snap, adv, path);
            await _proc.StartAsync(_acfg.ConfigFilePath, "127.0.0.1:15888", "linkroom", _acfg.CliFlags);

            if (isReconnect) _sm.ReconnectSucceeded();
            else _sm.EasyTierReady();
            StatusText = "已连接";
            StatusDetail = $"NAT:{snap.NatType} | {path.Strategy} | 端口:{adv.ListenerPort}";

            _ss.Save(SaveSettings());
            _ss.AddRoomHistory(room.RoomId);
            if (!RoomHistory.Contains(room.RoomId)) RoomHistory.Insert(0, room.RoomId);
            while (RoomHistory.Count > 5) RoomHistory.RemoveAt(RoomHistory.Count - 1);

            await (_mon?.CancelAsync() ?? Task.CompletedTask);
            _mon = new CancellationTokenSource();
            _ = MonitorAsync(_mon.Token);
            L($"已连接 room={room.RoomId} nat={snap.NatType} path={path.Strategy}");
        }
        catch (Exception ex)
        {
            L($"连接失败: {ex.Message}");
            StatusText = "连接失败";
            StatusDetail = ex.Message;
            if (!isReconnect) _sm.UserDisconnect();
            throw;
        }
        finally
        {
            _acfg?.Cleanup();
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    async Task CreateRoomAsync()
    {
        try
        {
            var id = GenId();
            var pw = _win?.GetCreatePassword() ?? "";
            if (string.IsNullOrEmpty(pw)) { pw = GenPw(); _win?.SetPasswordText(pw); }
            RoomId = id;
            Password = pw;
            L($"创建房间: {id}");
            var link = LinkCodeService.Encode(id, pw, GamePortHint);
            _win?.ShowCreatedRoom(id, link);

            try
            {
                Clipboard.SetText(LinkCodeService.ToClipboardText(id, pw, GamePortHint));
                L("联机信息已复制到剪贴板");
            }
            catch { }

            await ConnectInternalAsync(new RoomOptions { RoomId = id, Password = pw });
        }
        catch (Exception ex)
        {
            L($"创建失败: {ex.Message}");
            StatusText = "创建失败";
            StatusDetail = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    async Task ConnectAsync()
    {
        try
        {
            var decoded = LinkCodeService.Decode(RoomId.Trim());
            var rid = decoded.RoomId;
            if (!string.IsNullOrEmpty(decoded.Password)) Password = decoded.Password;
            if (decoded.Port is > 0) GamePortHint = decoded.Port;
            RoomId = rid;

            L($"加入房间: {rid}");
            await ConnectInternalAsync(new RoomOptions { RoomId = rid, Password = Password });
        }
        catch (Exception ex)
        {
            L($"加入失败: {ex.Message}");
            StatusText = "加入失败";
            StatusDetail = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    async Task DisconnectAsync()
    {
        await (_mon?.CancelAsync() ?? Task.CompletedTask);
        _sm.UserDisconnect();
        await _proc.StopAsync();
        EasyTierProcessService.KillOrphanProcesses();
        IsRelayMode = false;
        ConnectionQuality = "";
        PortForwardHint = "";
        StatusText = "已断开";
        StatusDetail = "";
        ConnState = "Disconnected";
        L("已断开连接");
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    async Task PingPeersAsync()
    {
        foreach (var p in Peers.ToList())
        {
            var raw = p.Split('|').FirstOrDefault()?.Trim() ?? "";
            var ip = raw.TrimStart(' ').Replace("👑", "").Replace("👤", "").Trim();
            if (string.IsNullOrEmpty(ip) || ip == "?") continue;
            var (ok, ms) = await _ping.PingAsync(ip);
            L(ok ? $"Ping {ip}: {ms}ms" : $"Ping {ip}: 失败");
        }
    }

    [RelayCommand]
    async Task ExportDiagnosticsAsync()
    {
        var snap = _dc.GetCached();
        var path = await _diag.ExportAsync(snap);
        L($"诊断包已导出: {path}");
        try { Clipboard.SetText(path); } catch { }
    }

    [RelayCommand]
    void OpenWebPanel()
    {
        if (_web.IsAvailable) { _web.Launch(); L("已启动 Web 管理面板"); }
        else L("Web 面板不可用");
    }

    [RelayCommand]
    void CopyLinkCode()
    {
        if (string.IsNullOrWhiteSpace(RoomId)) return;
        try { Clipboard.SetText(LinkCodeService.Encode(RoomId.Trim(), Password, GamePortHint)); L("联机链接已复制"); }
        catch { }
    }

    [RelayCommand]
    async Task RefreshStunListAsync()
    {
        L("正在更新 STUN 服务器列表...");
        await _stunProvider.RefreshRemoteListAsync();
        L("STUN 列表已更新");
    }

    [RelayCommand]
    async Task JoinHistoryRoomAsync(string? room)
    {
        if (string.IsNullOrWhiteSpace(room)) return;
        RoomId = room;
        await ConnectCommand.ExecuteAsync(null);
    }

    async Task MonitorAsync(CancellationToken ct)
    {
        var prevIds = new HashSet<string>();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(3000, ct);
                var ps = await _cli.GetPeersAsync(ct);
                var node = await _cli.GetNodeInfoAsync(ct);
                PeerCount = ps.Length;

                if (node != null)
                {
                    VirtualIpv4 = node.IPv4 ?? "";
                    VirtualIpv6 = node.IPv6 ?? "";
                    UpdatePortHint(node.IPv4);
                }

                var curIds = new HashSet<string>();
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    Peers.Clear();
                    for (int i = 0; i < ps.Length; i++)
                    {
                        var p = ps[i];
                        var id = p.IPv4 ?? p.Hostname ?? "?";
                        curIds.Add(id);
                        Peers.Add($"{(i == 0 ? "👑" : "👤")} {id} | {p.NatType ?? "?"} | {p.LatencyMs?.ToString("F0") ?? "?"}ms | {p.Cost ?? "?"}");
                    }
                    foreach (var old in prevIds)
                        if (!curIds.Contains(old) && prevIds.Count > 0)
                            LogLines.Add(SettingsService.SanitizeLog($"[{DateTime.Now:HH:mm:ss}] 📢 {old} 已断开"));
                    prevIds.Clear();
                    foreach (var c in curIds) prevIds.Add(c);
                });

                if (ps.Length > 0)
                {
                    var p = ps[0];
                    Latency = (p.LatencyMs?.ToString("F1") ?? "-") + "ms";
                    LossRate = p.LossRate?.ToString("P1") ?? "-";
                    ConnType = p.Cost ?? "";
                    IsRelayMode = p.Cost?.Contains("relay", StringComparison.OrdinalIgnoreCase) ?? false;
                    ConnectionQuality = IsRelayMode
                        ? $"⚠ 中继 | {Latency} | 丢包 {LossRate}"
                        : $"✅ P2P | {Latency} | 丢包 {LossRate}";
                    if (IsRelayMode) StatusDetail = "中继模式 — 建议 UPnP 或共享节点";
                    if (_sm.CurrentState == ConnectionState.Connected) _sm.Monitoring();
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    void UpdatePortHint(string? virtualIp)
    {
        if (string.IsNullOrEmpty(virtualIp)) return;
        var ports = GamePortScanner.ScanListeningGamePorts();
        var port = GamePortHint ?? (ports.Count > 0 ? ports[0].Port : 0);
        PortForwardHint = port > 0
            ? $"好友连接: {virtualIp}:{port}"
            : $"虚拟 IP: {virtualIp} — 开放游戏 LAN 后点扫描端口";
    }

    public async Task RunNatTestAsync(Action<string> report)
    {
        report("并发检测 NAT 类型...\n");
        var servers = _stunProvider.Resolve(CustomStunServers).Take(8).ToList();
        var tasks = servers.Select(s => _natProbe.ProbeWithDetailAsync(s.Host, s.Port, true, default)).ToList();

        while (tasks.Count > 0)
        {
            var done = await Task.WhenAny(tasks);
            tasks.Remove(done);
            var (host, r, err) = await done;
            if (r != null)
            {
                report($"✅ {host} → {r.NatType} ({r.PublicIPv4})\n");
                NatType = r.NatType.ToString();
                Ipv4 = r.PublicIPv4 ?? "";
                return;
            }
            report($"❌ {host}: {err ?? "超时"}\n");
        }
        report("⏱ 所有 STUN 无响应");
    }

    public string RunSelfCheck() => SelfCheckRunner.Run();

    partial void OnRoomIdChanged(string value) => ConnectCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        PasswordStrengthHint = PasswordStrength.Hint(PasswordStrength.Evaluate(value));
    }
    partial void OnPortableModeChanged(bool value) => AppPaths.Configure(value);
    partial void OnAutoStartChanged(bool value) => AutoStartService.SetEnabled(value);
    partial void OnMaxReconnectAttemptsChanged(int value) => _reconnect.MaxAttempts = value > 0 ? value : 5;
}

public static class SelfCheckRunner
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var pass = 0;
        var fail = 0;
        var rd = AppPaths.RuntimeDir;
        foreach (var f in new[] { "easytier-core.exe", "easytier-cli.exe", "wintun.dll" })
        {
            if (File.Exists(Path.Combine(rd, f))) { sb.AppendLine($"✅ {f}"); pass++; }
            else { sb.AppendLine($"❌ {f}"); fail++; }
        }
        sb.AppendLine($"📁 {AppPaths.DataRoot}");
        sb.AppendLine(AdminHelper.IsAdministrator() ? "✅ 管理员" : "⚠️ 非管理员");
        try
        {
            using var p = new System.Net.NetworkInformation.Ping();
            if (p.Send("8.8.8.8", 2000).Status == System.Net.NetworkInformation.IPStatus.Success)
            { sb.AppendLine("✅ 网络"); pass++; }
            else { sb.AppendLine("❌ 网络"); fail++; }
        }
        catch { sb.AppendLine("❌ 网络"); fail++; }
        sb.AppendLine($"\n{pass} 通过 / {fail} 失败");
        return sb.ToString();
    }
}
