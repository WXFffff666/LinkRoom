using System.IO;
using System.Windows;
using LinkRoom.Core;
using LinkRoom.Gui;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string runtimeDir;
        try { runtimeDir = RuntimeAssetExtractor.EnsureExtracted("2.6.4"); }
        catch (Exception ex) { MessageBox.Show($"EasyTier runtime failed: {ex.Message}\n\nRun as Administrator.", "LinkRoom"); Shutdown(); return; }

        var easytierCore = Path.Combine(runtimeDir, "easytier-core.exe");
        var easytierCli = Path.Combine(runtimeDir, "easytier-cli.exe");
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkRoom", "logs");
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
}