using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinkRoom.Core;

namespace LinkRoom.Gui;

public partial class MainViewModel
{
    CancellationTokenSource? _updateCts;
    bool _prevRelayMode;

    [ObservableProperty] bool _isUpnpDisabled = true, _autoCheckUpdate = true, _firstRunCompleted;
    [ObservableProperty] bool _ipv6Only, _enableSocks5, _roomLocked;
    [ObservableProperty] int _socks5Port = 1080;
    [ObservableProperty] string? _skippedUpdateVersion;
    [ObservableProperty] string _updateStatus = "", _pathDiagram = "", _shortLinkText = "";
    [ObservableProperty] bool _isProgressVisible;
    [ObservableProperty] double _progressValue;
    [ObservableProperty] string _progressText = "";

    public void SaveSettingsNow() => _ss.Save(SaveSettings());

    [RelayCommand]
    async Task CheckUpdateManualAsync()
    {
        UpdateStatus = "检查中...";
        try
        {
            var result = await _update.CheckAsync();
            if (!result.HasUpdate || result.Info == null)
            {
                UpdateStatus = $"已是最新 v{result.CurrentVersion}";
                return;
            }
            await PromptAndApplyUpdateAsync(result.Info);
        }
        catch (Exception ex)
        {
            UpdateStatus = "检查失败";
            L($"更新检查失败: {ex.Message}");
        }
    }

    public async Task CheckUpdateOnStartupAsync()
    {
        if (!AutoCheckUpdate) return;
        try
        {
            var result = await _update.CheckAsync();
            if (!result.HasUpdate || result.Info == null) return;
            if (string.Equals(result.Info.SemVer, SkippedUpdateVersion, StringComparison.OrdinalIgnoreCase)) return;

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await PromptAndApplyUpdateAsync(result.Info, isStartup: true);
            }).Task.ConfigureAwait(false);
        }
        catch { /* silent on startup */ }
    }

    async Task PromptAndApplyUpdateAsync(UpdateInfo info, bool isStartup = false)
    {
        var msg = $"发现新版本 {info.Tag}\n当前: v{UpdateService.CurrentVersion}\n大小: {info.SizeBytes / 1024 / 1024:F1} MB";
        if (!string.IsNullOrWhiteSpace(info.ReleaseNotes))
            msg += $"\n\n{info.ReleaseNotes[..Math.Min(200, info.ReleaseNotes.Length)]}...";

        var incremental = _update.IsIncrementalUpdate(info);
        if (incremental)
            msg += "\n\n✓ 可增量更新（保留 EasyTier 运行时）";

        var choice = MessageBox.Show(msg + "\n\n是=立即更新 | 否=跳过此版本 | 取消=稍后",
            "LinkRoom 更新", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);

        if (choice == MessageBoxResult.No)
        {
            SkippedUpdateVersion = info.SemVer;
            SaveSettingsNow();
            UpdateStatus = $"已跳过 {info.Tag}";
            return;
        }
        if (choice != MessageBoxResult.Yes) { UpdateStatus = $"有新版本 {info.Tag}"; return; }

        await DownloadAndApplyUpdateAsync(info);
    }

    async Task DownloadAndApplyUpdateAsync(UpdateInfo info)
    {
        _updateCts?.Cancel();
        _updateCts = new CancellationTokenSource();
        IsProgressVisible = true;
        ProgressValue = 0;
        ProgressText = "下载更新中...";
        UpdateStatus = "下载中...";

        try
        {
            var path = await _update.DownloadAsync(info, new Progress<UpdateDownloadProgress>(p =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    ProgressValue = p.Percent;
                    ProgressText = $"下载 {p.Percent:F0}% ({p.Received / 1024 / 1024:F1}/{p.Total / 1024 / 1024:F1} MB)";
                });
            }), _updateCts.Token);

            ProgressText = "准备安装并重启...";
            L($"更新已下载: {path}");
            UpdateStatus = "正在应用更新...";

            await DisconnectAsync();
            _update.ApplyAndRestart(path);
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = "下载已取消";
            L("更新下载已取消");
        }
        catch (Exception ex)
        {
            UpdateStatus = "更新失败";
            L($"更新失败: {ex.Message}");
            MessageBox.Show($"更新失败: {ex.Message}", "LinkRoom");
        }
        finally
        {
            IsProgressVisible = false;
        }
    }

    [RelayCommand]
    void CopyVirtualIp()
    {
        if (string.IsNullOrWhiteSpace(VirtualIpv4)) return;
        try { Clipboard.SetText(VirtualIpv4); L($"已复制虚拟 IP: {VirtualIpv4}"); }
        catch { }
    }

    [RelayCommand]
    async Task SpeedTestAsync()
    {
        if (string.IsNullOrWhiteSpace(VirtualIpv4)) { L("请先连接房间"); return; }
        var port = GamePortHint ?? 25565;
        L($"测速 {VirtualIpv4}:{port}...");
        var (ok, ms, detail) = await SpeedTestService.TestTcpAsync(VirtualIpv4, port);
        L(ok ? $"✅ {detail}" : $"❌ 测速失败: {detail}");
    }

    [RelayCommand]
    async Task ExportConfigAsync()
    {
        var path = Path.Combine(AppPaths.ConfigDir, $"linkroom-export-{DateTime.Now:yyyyMMdd-HHmmss}.linkroom.json");
        await ConfigImportExportService.ExportToFileAsync(SaveSettings(), path);
        L($"配置已导出: {path}");
        try { Clipboard.SetText(path); } catch { }
    }

    [RelayCommand]
    async Task ImportConfigAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "LinkRoom 配置|*.linkroom.json;*.json|所有文件|*.*",
            Title = "导入配置",
        };
        if (dlg.ShowDialog() != true) return;
        var imported = await ConfigImportExportService.ImportFromFileAsync(dlg.FileName);
        RestoreSettings(imported);
        SaveSettingsNow();
        L("配置已导入");
    }

    [RelayCommand]
    void ScanMods()
    {
        var r = ModDetectorService.ScanMinecraft();
        L(r.TotalCount > 0
            ? $"检测到 {r.TotalCount} 个 MC Mod: {string.Join(", ", r.SampleNames.Take(5))}"
            : "未检测到 Minecraft Mod");
    }

    [RelayCommand]
    async Task CheckEasyTierVersionAsync()
    {
        var latest = await EasyTierUpdateService.CheckLatestEasyTierVersionAsync();
        var embedded = EasyTierUpdateService.EmbeddedVersion;
        L(latest == null ? "无法检查 EasyTier 版本"
            : latest == embedded ? $"EasyTier 已是最新 v{embedded}"
            : $"EasyTier 当前 v{embedded}，最新 {latest}");
    }

    [RelayCommand]
    void RefreshNetwork()
    {
        _dc.Invalidate();
        L("网络检测缓存已刷新，下次连接将重新检测");
    }

    partial void OnIsUpnpDisabledChanged(bool value) => SaveSettingsNow();
    partial void OnAutoCheckUpdateChanged(bool value) => SaveSettingsNow();
    partial void OnIpv6OnlyChanged(bool value) => SaveSettingsNow();
    partial void OnEnableSocks5Changed(bool value) => SaveSettingsNow();
    partial void OnSocks5PortChanged(int value) => SaveSettingsNow();
    partial void OnRoomLockedChanged(bool value) => SaveSettingsNow();
    partial void OnListenerPortChanged(int value) => SaveSettingsNow();
    partial void OnMtuChanged(int value) => SaveSettingsNow();
    partial void OnUseLanModeChanged(bool value) { AppPaths.Configure(PortableMode); SaveSettingsNow(); }
    partial void OnIsSharedNodeEnabledChanged(bool value) => SaveSettingsNow();
    partial void OnSharedNodeUrlsChanged(string value) => SaveSettingsNow();
    partial void OnCustomStunServersChanged(string value) => SaveSettingsNow();
    partial void OnPreferIPv6Changed(bool value) => SaveSettingsNow();
    partial void OnDarkModeChanged(bool value) => SaveSettingsNow();
    partial void OnIsHostModeChanged(bool value) => SaveSettingsNow();
}
