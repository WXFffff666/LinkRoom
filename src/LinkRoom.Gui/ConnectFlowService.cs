using System.Collections.Generic;
using System.Linq;
using System.Windows;
using LinkRoom.Core;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Gui;

/// <summary>
/// Owns the connect flow: NAT detection → path selection → EasyTier launch,
/// connection progress, the monitor loop, reconnection (single orchestrator +
/// backoff) and P2P path visualization. Pure move out of MainViewModel —
/// no logic changes. VM state is read/written through the MainViewModel
/// reference (shared-state object), avoiding service-to-service cycles.
/// </summary>
internal sealed class ConnectFlowService
{
    readonly MainViewModel _vm;
    readonly EasyTierConfigBuilder _cfg;
    readonly EasyTierProcessService _proc;
    readonly EasyTierCliClient _cli;
    readonly ConnectionStateMachine _sm;
    readonly PathSelectionStrategy _ps;
    readonly DetectionCache _dc;
    readonly NetworkInfoService _ns;
    readonly SettingsService _ss;
    readonly ProcessGuardian _guardian;
    readonly SettingsFacade _settings;
    readonly ReconnectOrchestrator _reconnectOrch = new();
    readonly AutoReconnectService _reconnect;

    CancellationTokenSource? _mon;
    EasyTierLaunchConfig? _acfg;
    RoomOptions? _lastRoom;
    bool _prevRelayMode;

    public ConnectFlowService(
        MainViewModel vm,
        EasyTierConfigBuilder cfg, EasyTierProcessService proc, EasyTierCliClient cli,
        ConnectionStateMachine sm, PathSelectionStrategy ps, DetectionCache dc,
        NetworkInfoService ns, SettingsService ss, ProcessGuardian guardian,
        SettingsFacade settings, ILogger<AutoReconnectService> reconnectLog)
    {
        _vm = vm; _cfg = cfg; _proc = proc; _cli = cli; _sm = sm; _ps = ps;
        _dc = dc; _ns = ns; _ss = ss; _guardian = guardian; _settings = settings;
        _reconnect = new AutoReconnectService(sm, ReconnectAsync, reconnectLog);
    }

    internal AutoReconnectService Reconnect => _reconnect;

    internal async Task CancelMonitorAsync()
    {
        if (_mon != null) await _mon.CancelAsync();
    }

    // Marshal a synchronous action onto the WPF UI thread (no-op if no Application).
    // Fire-and-forget: the operation runs on the UI thread; errors are handled by
    // the action itself (the operation's Task is intentionally not awaited here).
    internal void Ui(Action a)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        _ = dispatcher.InvokeAsync(a);
    }

    // Marshal an async action onto the UI thread and await its completion,
    // unwrapping the inner Task so exceptions propagate to the caller.
    internal Task UiAsync(Func<Task> f)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return Task.CompletedTask;
        return dispatcher.InvokeAsync(f).Task.Unwrap();
    }

    // Single reconnect orchestrator (BUG-5): every reconnect path funnels through
    // this wrapper so at most one ConnectInternalAsync(isReconnect: true) executes
    // at a time. Requests arriving while one is running coalesce — the later caller
    // is skipped when the process is already back, preventing the old livelock where
    // the guardian callback and AutoReconnectService both killed each other's core.
    Task ReconnectOnceAsync(Func<Task> reconnect)
        => _reconnectOrch.RunOnceAsync(reconnect, () => _proc.IsRunning);

    // AutoReconnectService runs its backoff loop on the thread pool —
    // the reconnect must execute on the UI thread (BUG-2 fix).
    async Task ReconnectAsync(CancellationToken ct)
    {
        if (_lastRoom == null) return;
        await ReconnectOnceAsync(() => UiAsync(() => ConnectInternalAsync(_lastRoom, isReconnect: true)));
    }

    internal async Task ConnectInternalAsync(RoomOptions room, bool isReconnect = false)
    {
        var (valid, err) = ConfigValidator.ValidateAll(room.RoomId, _vm.ListenerPort, _vm.Mtu, room.Password);
        if (!valid)
        {
            _vm.StatusText = "参数无效";
            _vm.StatusDetail = err ?? "";
            _vm.L(err ?? "参数无效");
            return;
        }

        if (_vm.RoomLocked && !_vm.IsHostMode)
        {
            _vm.StatusText = "房间已锁定";
            _vm.StatusDetail = "房主已锁定房间，无法加入";
            _vm.L("房间已锁定，加入被拒绝");
            return;
        }

        if (_vm.UseLanMode && !AdminHelper.IsAdministrator())
        {
            _vm.StatusText = "需要管理员权限";
            _vm.StatusDetail = "LAN 模式需要管理员权限，请右键以管理员运行";
            return;
        }

        var adv = _settings.Adv();
        _lastRoom = room;
        _reconnect.MaxAttempts = adv.MaxReconnectAttempts;
        _ns.SetCustomStunServers(adv.CustomStunServers);
        if (!isReconnect) _dc.Invalidate();

        if (!isReconnect) _sm.UserConnect();
        _vm.StatusText = isReconnect ? "重连中..." : "连接中...";
        _vm.StatusDetail = "正在检测 NAT 类型...";
        _vm.IsProgressVisible = true;
        _vm.ProgressValue = 10;
        _vm.ProgressText = "检测网络...";

        try
        {
            if (isReconnect)
            {
                await _proc.StopAsync();
                EasyTierProcessService.KillOrphanProcesses();
            }

            _vm.ProgressValue = 30;
            _vm.ProgressText = "NAT 检测中...";
            var snap = await _dc.GetAsync();
            _vm.NatType = snap.NatType.ToString();
            _vm.Ipv4 = snap.PublicIPv4 ?? "";
            _vm.Ipv6 = snap.PublicIPv6 ?? "";

            if (!isReconnect) _sm.DetectionComplete();

            _vm.ProgressValue = 50;
            _vm.ProgressText = "选择连接路径...";
            var path = _ps.Evaluate(snap, adv);
            foreach (var w in path.Warnings) _vm.L($"⚠ {w}");
            _vm.PathDiagram = PathVisualizationService.Build(_vm.NatType, path.Strategy, _vm.ConnType, _vm.IsRelayMode, !_vm.IsUpnpDisabled);

            _vm.ProgressValue = 70;
            _vm.ProgressText = "启动 EasyTier...";
            _acfg = await _cfg.BuildAsync(room, snap, adv, path);
            await _proc.StartAsync(_acfg.ConfigFilePath, "127.0.0.1:15888", "linkroom", _acfg.CliFlags);

            if (isReconnect) _sm.ReconnectSucceeded();
            else _sm.EasyTierReady();
            _vm.ProgressValue = 100;
            _vm.ProgressText = "已连接";
            _vm.StatusText = "已连接";
            _vm.StatusDetail = $"NAT:{snap.NatType} | {path.Strategy} | 端口:{adv.ListenerPort}";

            _ss.Save(_settings.SaveSettings());
            _ss.AddRoomHistory(room.RoomId);
            if (!_vm.RoomHistory.Contains(room.RoomId)) _vm.RoomHistory.Insert(0, room.RoomId);
            while (_vm.RoomHistory.Count > 5) _vm.RoomHistory.RemoveAt(_vm.RoomHistory.Count - 1);

            // Guardian's WatchAsync ticks on the thread pool — run the recovery
            // on the UI thread; the returned Task propagates exceptions to the
            // guardian's own catch (BUG-2 fix).
            _guardian.Start(() => UiAsync(async () =>
            {
                // While the state machine is already Reconnecting, AutoReconnectService
                // owns recovery — a guardian fire would only race it (BUG-5).
                if (_sm.CurrentState is not (ConnectionState.Connected or ConnectionState.Monitoring))
                    return;
                _vm.L("EasyTier 进程异常，尝试恢复...");
                if (_lastRoom != null)
                    await ReconnectOnceAsync(() => ConnectInternalAsync(_lastRoom, isReconnect: true));
            }));

            await (_mon?.CancelAsync() ?? Task.CompletedTask);
            _mon = new CancellationTokenSource();
            _ = MonitorAsync(_mon.Token);
            _vm.L($"已连接 room={room.RoomId} nat={snap.NatType} path={path.Strategy}");
            StartChat(room.RoomId);
        }
        catch (Exception ex)
        {
            _vm.L($"连接失败: {ex.Message}");
            // Secure mode must not fail silently (spike: easytier-core rejects
            // peers that lack secure mode / mismatched pins; 2.6.4 config-file
            // path needs the persisted keypair). Surface actionable guidance
            // instead of a bare error string.
            if (adv.EnableSecureMode)
            {
                var hint = "安全模式提示：请确认网络内所有节点均为最新版且已开启安全模式；若填写了共享节点公钥，请确认其正确（锁定失败会被拒绝）。";
                _vm.L(hint);
                _vm.StatusDetail = ex.Message + "（" + hint + "）";
            }
            else
            {
                _vm.StatusDetail = ex.Message;
            }
            _vm.StatusText = "连接失败";
            if (!isReconnect) _sm.UserDisconnect();
            throw;
        }
        finally
        {
            _vm.IsProgressVisible = false;
            _acfg?.Cleanup();
            _vm.ConnectCommand.NotifyCanExecuteChanged();
            _vm.DisconnectCommand.NotifyCanExecuteChanged();
        }
    }

    internal async Task MonitorAsync(CancellationToken ct)
    {
        var prevIds = new HashSet<string>();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(3000, ct);
                var ps = await _cli.GetPeersAsync(ct);
                var node = await _cli.GetNodeInfoAsync(ct);
                _vm.PeerCount = ps.Length;

                if (node != null)
                {
                    _vm.VirtualIpv4 = node.IPv4 ?? "";
                    _vm.VirtualIpv6 = node.IPv6 ?? "";
                    UpdatePortHint(node.IPv4);
                }

                var curIds = new HashSet<string>();
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    _vm.Peers.Clear();
                    for (int i = 0; i < ps.Length; i++)
                    {
                        var p = ps[i];
                        var id = p.IPv4 ?? p.Hostname ?? "?";
                        curIds.Add(id);
                        _vm.Peers.Add($"{(i == 0 ? "👑" : "👤")} {id} | {p.NatType ?? "?"} | {p.LatencyMs?.ToString("F0") ?? "?"}ms | {p.Cost ?? "?"}");
                    }
                    foreach (var old in prevIds)
                        if (!curIds.Contains(old) && prevIds.Count > 0)
                        {
                            _vm.L($"📢 {old} 已离开");
                            NotificationService.Show("LinkRoom", $"{old} 已离开房间");
                        }
                    foreach (var id in curIds)
                        if (!prevIds.Contains(id) && prevIds.Count > 0 && id != "?")
                            NotificationService.Show("LinkRoom", $"{id} 加入了房间");
                    prevIds.Clear();
                    foreach (var c in curIds) prevIds.Add(c);
                });

                if (ps.Length > 0)
                {
                    var p = ps[0];
                    _vm.Latency = (p.LatencyMs?.ToString("F1") ?? "-") + "ms";
                    _vm.LossRate = p.LossRate?.ToString("P1") ?? "-";
                    _vm.ConnType = p.Cost ?? "";
                    var isRelay = p.Cost?.Contains("relay", StringComparison.OrdinalIgnoreCase) ?? false;
                    _vm.IsRelayMode = isRelay;
                    _vm.ConnectionQuality = isRelay
                        ? $"⚠ 中继 | {_vm.Latency} | 丢包 {_vm.LossRate}"
                        : $"✅ P2P | {_vm.Latency} | 丢包 {_vm.LossRate}";
                    _vm.PathDiagram = PathVisualizationService.Build(_vm.NatType, _vm.ConnType, p.Cost ?? "", isRelay, !_vm.IsUpnpDisabled);
                    if (isRelay)
                    {
                        _vm.StatusDetail = "中继模式 — 建议 UPnP 或共享节点";
                        if (!_prevRelayMode) NotificationService.Show("LinkRoom", "已切换到中继模式");
                    }
                    _prevRelayMode = isRelay;
                    if (_sm.CurrentState == ConnectionState.Connected) _sm.Monitoring();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _vm.L($"监控错误: {ex.Message}"); }
        }
    }

    void UpdatePortHint(string? virtualIp)
    {
        if (string.IsNullOrEmpty(virtualIp)) return;
        var ports = GamePortScanner.ScanListeningGamePorts();
        var port = _vm.GamePortHint ?? (ports.Count > 0 ? ports[0].Port : 0);
        _vm.PortForwardHint = port > 0
            ? $"好友连接: {virtualIp}:{port}"
            : $"虚拟 IP: {virtualIp} — 开放游戏 LAN 后点扫描端口";
    }

    // Starts the in-room chat once the virtual network is up. Host binds its own
    // virtual IP (NodeInfo.IPv4); guests connect to the host's virtual IP from
    // the peer list. Best-effort: chat failure must never break the connection.
    // StartAsync is restart-safe (stops any previous session first), so this also
    // runs on reconnect paths.
    async void StartChat(string roomId)
    {
        try
        {
            var node = await _cli.GetNodeInfoAsync();
            var ip = node?.IPv4;
            if (string.IsNullOrEmpty(ip))
            {
                _vm.L("聊天不可用：未获取到虚拟 IP");
                return;
            }

            if (_vm.IsHostMode)
            {
                await _vm.Chat.StartAsync(isHost: true, ip);
            }
            else
            {
                var peers = await _cli.GetPeersAsync();
                var hostIp = peers.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.IPv4))?.IPv4;
                hostIp = hostIp?.Split('/')[0]; // strip any CIDR suffix
                if (string.IsNullOrEmpty(hostIp))
                {
                    _vm.L("聊天不可用：未找到房主虚拟 IP");
                    return;
                }
                await _vm.Chat.StartAsync(isHost: false, hostIp);
            }
            _vm.L($"房间聊天已启动 room={roomId}");
        }
        catch (Exception ex)
        {
            _vm.L($"聊天启动失败: {ex.Message}");
        }
    }
}
