using System.IO;
using System.Text;
using System.Windows;
using LinkRoom.Core;
using LinkRoom.Core.Resources;
using LinkRoom.Network;
using Microsoft.Win32;

namespace LinkRoom.Gui;

/// <summary>
/// Owns update check/download, network utilities (speed test, ping, STUN
/// refresh), diagnostics export, config import/export, mod scan, EasyTier
/// version check and self-check. Pure move out of MainViewModel — no logic
/// changes. Depends on SettingsFacade (config) and RoomSessionService
/// (disconnect-before-restart).
/// </summary>
internal sealed class UpdateAndToolsService
{
    readonly MainViewModel _vm;
    readonly UpdateService _update;
    readonly DetectionCache _dc;
    readonly DiagnosticsService _diag;
    readonly WebPanelService _web;
    readonly StunServerProvider _stunProvider;
    readonly NatProbeService _natProbe;
    readonly PeerPingService _ping;
    readonly SettingsFacade _settings;
    readonly RoomSessionService _room;

    CancellationTokenSource? _updateCts;

    public UpdateAndToolsService(
        MainViewModel vm, UpdateService update, DetectionCache dc,
        DiagnosticsService diag, WebPanelService web, StunServerProvider stunProvider,
        NatProbeService natProbe, PeerPingService ping,
        SettingsFacade settings, RoomSessionService room)
    {
        _vm = vm; _update = update; _dc = dc; _diag = diag; _web = web;
        _stunProvider = stunProvider; _natProbe = natProbe; _ping = ping;
        _settings = settings; _room = room;
    }

    internal async Task CheckUpdateManualAsync()
    {
        _vm.UpdateStatus = "检查中...";
        try
        {
            var result = await _update.CheckAsync();
            if (!result.HasUpdate || result.Info == null)
            {
                _vm.UpdateStatus = $"已是最新 v{result.CurrentVersion}";
                return;
            }
            await PromptAndApplyUpdateAsync(result.Info);
        }
        catch (Exception ex)
        {
            _vm.UpdateStatus = "检查失败";
            _vm.L($"更新检查失败: {ex.Message}");
        }
    }

    internal async Task CheckUpdateOnStartupAsync()
    {
        if (!_vm.AutoCheckUpdate) return;
        try
        {
            var result = await _update.CheckAsync();
            if (!result.HasUpdate || result.Info == null) return;
            if (string.Equals(result.Info.SemVer, _vm.SkippedUpdateVersion, StringComparison.OrdinalIgnoreCase)) return;

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await PromptAndApplyUpdateAsync(result.Info, isStartup: true);
            }).Task.ConfigureAwait(false);
        }
        catch { /* silent on startup */ }
    }

    async Task PromptAndApplyUpdateAsync(UpdateInfo info, bool isStartup = false)
    {
        var msg = string.Format(Strings.MsgUpdateFoundTemplate,
            info.Tag, UpdateService.CurrentVersion, info.SizeBytes / 1024d / 1024d);
        if (!string.IsNullOrWhiteSpace(info.ReleaseNotes))
            msg += $"\n\n{info.ReleaseNotes[..Math.Min(200, info.ReleaseNotes.Length)]}...";

        var incremental = _update.IsIncrementalUpdate(info);
        if (incremental)
            msg += Strings.MsgUpdateIncremental;

        var choice = MessageBox.Show(msg + Strings.MsgUpdateOptions,
            Strings.MsgUpdateTitle, MessageBoxButton.YesNoCancel, MessageBoxImage.Information);

        if (choice == MessageBoxResult.No)
        {
            _vm.SkippedUpdateVersion = info.SemVer;
            _settings.SaveSettingsNow();
            _vm.UpdateStatus = $"已跳过 {info.Tag}";
            return;
        }
        if (choice != MessageBoxResult.Yes) { _vm.UpdateStatus = $"有新版本 {info.Tag}"; return; }

        await DownloadAndApplyUpdateAsync(info);
    }

    async Task DownloadAndApplyUpdateAsync(UpdateInfo info)
    {
        _updateCts?.Cancel();
        _updateCts = new CancellationTokenSource();
        _vm.IsProgressVisible = true;
        _vm.ProgressValue = 0;
        _vm.ProgressText = "下载更新中...";
        _vm.UpdateStatus = "下载中...";

        try
        {
            var path = await _update.DownloadAsync(info, new Progress<UpdateDownloadProgress>(p =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _vm.ProgressValue = p.Percent;
                    _vm.ProgressText = $"下载 {p.Percent:F0}% ({p.Received / 1024 / 1024:F1}/{p.Total / 1024 / 1024:F1} MB)";
                });
            }), _updateCts.Token);

            _vm.ProgressText = "准备安装并重启...";
            _vm.L($"更新已下载: {path}");
            _vm.UpdateStatus = "正在应用更新...";

            await _room.DisconnectAsync();
            _update.ApplyAndRestart(path);
        }
        catch (OperationCanceledException)
        {
            _vm.UpdateStatus = "下载已取消";
            _vm.L("更新下载已取消");
        }
        catch (Exception ex)
        {
            _vm.UpdateStatus = "更新失败";
            _vm.L($"更新失败: {ex.Message}");
            MessageBox.Show(string.Format(Strings.MsgUpdateFailed, ex.Message), "LinkRoom");
        }
        finally
        {
            _vm.IsProgressVisible = false;
        }
    }

    internal void CopyVirtualIp()
    {
        if (string.IsNullOrWhiteSpace(_vm.VirtualIpv4)) return;
        try { Clipboard.SetText(_vm.VirtualIpv4); _vm.L($"已复制虚拟 IP: {_vm.VirtualIpv4}"); }
        catch { }
    }

    internal async Task SpeedTestAsync()
    {
        if (string.IsNullOrWhiteSpace(_vm.VirtualIpv4)) { _vm.L("请先连接房间"); return; }
        var port = _vm.GamePortHint ?? 25565;
        _vm.L($"测速 {_vm.VirtualIpv4}:{port}...");
        var (ok, ms, detail) = await SpeedTestService.TestTcpAsync(_vm.VirtualIpv4, port);
        _vm.L(ok ? $"✅ {detail}" : $"❌ 测速失败: {detail}");
    }

    internal async Task ExportConfigAsync()
    {
        var path = Path.Combine(AppPaths.ConfigDir, $"linkroom-export-{DateTime.Now:yyyyMMdd-HHmmss}.linkroom.json");
        await ConfigImportExportService.ExportToFileAsync(_settings.SaveSettings(), path);
        _vm.L($"配置已导出: {path}");
        try { Clipboard.SetText(path); } catch { }
    }

    internal async Task ImportConfigAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "LinkRoom 配置|*.linkroom.json;*.json|所有文件|*.*",
            Title = "导入配置",
        };
        if (dlg.ShowDialog() != true) return;
        var imported = await ConfigImportExportService.ImportFromFileAsync(dlg.FileName);
        _settings.RestoreSettings(imported);
        _settings.SaveSettingsNow();
        _vm.L("配置已导入");
    }

    internal void ScanMods()
    {
        var r = ModDetectorService.ScanMinecraft();
        _vm.L(r.TotalCount > 0
            ? $"检测到 {r.TotalCount} 个 MC Mod: {string.Join(", ", r.SampleNames.Take(5))}"
            : "未检测到 Minecraft Mod");
    }

    internal async Task CheckEasyTierVersionAsync()
    {
        var latest = await EasyTierUpdateService.CheckLatestEasyTierVersionAsync();
        var embedded = EasyTierUpdateService.EmbeddedVersion;
        _vm.L(latest == null ? "无法检查 EasyTier 版本"
            : latest == embedded ? $"EasyTier 已是最新 v{embedded}"
            : $"EasyTier 当前 v{embedded}，最新 {latest}");
    }

    internal void RefreshNetwork()
    {
        _dc.Invalidate();
        _vm.L("网络检测缓存已刷新，下次连接将重新检测");
    }

    internal void DetectGame()
    {
        var hits = GameDetectorService.DetectRunningGames();
        _vm.DetectedGames = hits;
        _vm.GameDetectResult = hits.Count == 0
            ? "未检测到运行中的游戏"
            : $"检测到 {hits.Count} 个游戏，点击下方游戏名一键开房";
        _vm.L(hits.Count == 0
            ? "游戏检测: 未检测到运行中的游戏"
            : $"游戏检测: {string.Join("、", hits.Select(h => $"{h.GameName}({h.ProcessName}.exe)"))}");
    }

    internal void ApplyDetectedGame(GameProcessInfo? game)
    {
        if (game == null) return;
        _vm.GamePortHint = game.Port;
        _vm.PortForwardHint = string.IsNullOrWhiteSpace(_vm.VirtualIpv4)
            ? $"游戏端口: {game.Port}（{game.GameName}）— 连接房间后自动显示好友连接地址"
            : $"好友连接: {_vm.VirtualIpv4}:{game.Port}";
        _settings.SaveSettingsNow();
        _vm.L($"一键开房: {game.GameName} 端口 {game.Port} 已设置并保存");
    }

    internal async Task PingPeersAsync()
    {
        foreach (var p in _vm.Peers.ToList())
        {
            var raw = p.Split('|').FirstOrDefault()?.Trim() ?? "";
            var ip = raw.TrimStart(' ').Replace("👑", "").Replace("👤", "").Trim();
            if (string.IsNullOrEmpty(ip) || ip == "?") continue;
            var (ok, ms) = await _ping.PingAsync(ip);
            _vm.L(ok ? $"Ping {ip}: {ms}ms" : $"Ping {ip}: 失败");
        }
    }

    internal async Task ExportDiagnosticsAsync()
    {
        var snap = _dc.GetCached();
        var path = await _diag.ExportAsync(snap);
        _vm.L($"诊断包已导出: {path}");
        try { Clipboard.SetText(path); } catch { }
    }

    internal void OpenWebPanel()
    {
        if (_web.IsAvailable) { _web.Launch(); _vm.L("已启动 Web 管理面板"); }
        else _vm.L("Web 面板不可用");
    }

    internal void CopyLinkCode()
    {
        if (string.IsNullOrWhiteSpace(_vm.RoomId)) return;
        try { Clipboard.SetText(LinkCodeService.Encode(_vm.RoomId.Trim(), _vm.Password, _vm.GamePortHint, _vm.LockSecret)); _vm.L("联机链接已复制"); }
        catch { }
    }

    internal async Task RefreshStunListAsync()
    {
        _vm.L("正在更新 STUN 服务器列表...");
        await _stunProvider.RefreshRemoteListAsync();
        _vm.L("STUN 列表已更新");
    }

    internal async Task RunNatTestAsync(Action<string> report)
    {
        report("并发检测 NAT 类型...\n");
        var servers = _stunProvider.Resolve(_vm.CustomStunServers).Take(8).ToList();
        var tasks = servers.Select(s => _natProbe.ProbeWithDetailAsync(s.Host, s.Port, true, default)).ToList();

        while (tasks.Count > 0)
        {
            var done = await Task.WhenAny(tasks);
            tasks.Remove(done);
            var (host, r, err) = await done;
            if (r != null)
            {
                report($"✅ {host} → {r.NatType} ({r.PublicIPv4})\n");
                _vm.NatType = r.NatType.ToString();
                _vm.Ipv4 = r.PublicIPv4 ?? "";
                return;
            }
            report($"❌ {host}: {err ?? "超时"}\n");
        }
        report("⏱ 所有 STUN 无响应");
    }

    internal string RunSelfCheck() => SelfCheckRunner.Run();
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
