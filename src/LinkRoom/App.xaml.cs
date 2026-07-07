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

        // 1. Extract EasyTier runtime assets on first launch
        string runtimeDir;
        try
        {
            runtimeDir = RuntimeAssetExtractor.EnsureExtracted("2.6.4");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法解压 EasyTier 运行时: {ex.Message}\n\n请以管理员身份运行。",
                "LinkRoom", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var easytierCorePath = System.IO.Path.Combine(runtimeDir, "easytier-core.exe");
        var easytierCliPath = System.IO.Path.Combine(runtimeDir, "easytier-cli.exe");
        var logDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkRoom", "logs");

        // 2. Build logger
        using var loggerFactory = LoggerFactory.Create(b =>
        {
            b.AddDebug();
            b.SetMinimumLevel(LogLevel.Information);
        });

        // 3. Create core services
        var processService = new EasyTierProcessService(easytierCorePath, logDir,
            loggerFactory.CreateLogger<EasyTierProcessService>());
        var cliClient = new EasyTierCliClient(easytierCliPath, "127.0.0.1:15888",
            loggerFactory.CreateLogger<EasyTierCliClient>());
        var configBuilder = new EasyTierConfigBuilder(loggerFactory.CreateLogger<EasyTierConfigBuilder>());
        var stateMachine = new ConnectionStateMachine(loggerFactory.CreateLogger<ConnectionStateMachine>());
        var pathSelector = new PathSelectionStrategy(loggerFactory.CreateLogger<PathSelectionStrategy>());

        // 4. Create network detection services
        var natDetector = new StunNatDetector(loggerFactory.CreateLogger<StunNatDetector>());
        var networkService = new NetworkInfoService(natDetector,
            loggerFactory.CreateLogger<NetworkInfoService>());
        var detectionCache = new DetectionCache(networkService,
            loggerFactory.CreateLogger<DetectionCache>());

        // 5. Create settings + reconnect
        var settingsService = new SettingsService();
        var savedSettings = settingsService.Load();
        var autoReconnect = new AutoReconnectService(stateMachine,
            ct => Task.CompletedTask, // wired later by ViewModel
            loggerFactory.CreateLogger<AutoReconnectService>());

        // 6. Create ViewModel with full service graph
        var viewModel = new MainViewModel(
            configBuilder, processService, cliClient,
            stateMachine, pathSelector, detectionCache,
            networkService, settingsService,
            loggerFactory.CreateLogger<MainViewModel>());

        // 7. Restore saved settings
        viewModel.RestoreSettings(savedSettings);

        // 8. Show window
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
    }
}