using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using LinkRoom.Core;
using LinkRoom.Gui;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom;

public partial class App : Application
{
    const string CurrentVersion = "v1.12.0";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Exit += (_, _) => EasyTierProcessService.KillOrphanProcesses();

        // Check for updates in background
        _ = CheckForUpdatesAsync();

        string runtimeDir;
        try { runtimeDir = RuntimeAssetExtractor.EnsureExtracted("2.6.4"); }
        catch (Exception ex) { MessageBox.Show($"EasyTier runtime failed: {ex.Message}\n\nRun as Administrator.", "LinkRoom"); Shutdown(); return; }

        var easytierCore = Path.Combine(runtimeDir, "easytier-core.exe");
        var easytierCli = Path.Combine(runtimeDir, "easytier-cli.exe");
        var logDir = Path.Combine(runtimeDir, "..", "logs");
        var logFile = Path.Combine(logDir, "linkroom.log");

        var logSink = new RollingLogSink(logFile, 500);
        using var loggerFactory = LoggerFactory.Create(b => { b.AddProvider(logSink); b.SetMinimumLevel(LogLevel.Information); });

        var processService = new EasyTierProcessService(easytierCore, logDir, loggerFactory.CreateLogger<EasyTierProcessService>());
        var cliClient = new EasyTierCliClient(easytierCli, "127.0.0.1:15888", loggerFactory.CreateLogger<EasyTierCliClient>());
        var configBuilder = new EasyTierConfigBuilder(loggerFactory.CreateLogger<EasyTierConfigBuilder>());
        var stateMachine = new ConnectionStateMachine(loggerFactory.CreateLogger<ConnectionStateMachine>());
        var pathSelector = new PathSelectionStrategy(loggerFactory.CreateLogger<PathSelectionStrategy>());
        var natDetector = new StunNatDetector(loggerFactory.CreateLogger<StunNatDetector>());
        var networkService = new NetworkInfoService(natDetector, loggerFactory.CreateLogger<NetworkInfoService>());
        var detectionCache = new DetectionCache(networkService, loggerFactory.CreateLogger<DetectionCache>());
        var settingsService = new SettingsService();
        var saved = settingsService.Load();

        var vm = new MainViewModel(configBuilder, processService, cliClient, stateMachine, pathSelector,
            detectionCache, networkService, settingsService,
            loggerFactory.CreateLogger<MainViewModel>());
        vm.RestoreSettings(saved);

        var window = new MainWindow { DataContext = vm };
        vm.SetWindow(window);
        window.Show();
    }

    static async Task CheckForUpdatesAsync()
    {
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            hc.DefaultRequestHeaders.UserAgent.ParseAdd("LinkRoom");
            var json = await hc.GetStringAsync("https://api.github.com/repos/WXFffff666/LinkRoom/releases/latest");
            var tag = JsonDocument.Parse(json).RootElement.GetProperty("tag_name").GetString();
            if (tag != null && tag != CurrentVersion)
            {
                var result = MessageBox.Show($"发现新版本 {tag}\n当前: {CurrentVersion}\n\n是否打开下载页面？",
                    "LinkRoom 更新", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        { FileName = $"https://github.com/WXFffff666/LinkRoom/releases/tag/{tag}", UseShellExecute = true });
            }
        }
        catch { /* network error — skip silently */ }
    }
}