using System.IO;
using System.Windows;
using LinkRoom.Core;
using LinkRoom.Gui;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom;

public partial class App : Application
{
    public static string Version { get; } =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.16.0";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Exit += (_, _) => EasyTierProcessService.KillOrphanProcesses();

        MessageBoxCompat.ShowHandler = (msg, title) =>
            MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Information);

        var cli = CliRunner.Parse(e.Args);
        var settingsService = new SettingsService();
        var saved = settingsService.Load();
        AppPaths.Configure(saved.PortableMode);
        AppPaths.EnsureDataDirectories();
        StunServerProvider.CachePathOverride = AppPaths.StunCachePath;
        PluginRegistry.LoadFromDirectory(AppPaths.PluginsDir);

        string runtimeDir;
        try { runtimeDir = RuntimeAssetExtractor.EnsureExtracted(); }
        catch (Exception ex)
        {
            MessageBox.Show($"EasyTier 运行时解压失败: {ex.Message}", "LinkRoom");
            Shutdown();
            return;
        }

        var logFile = Path.Combine(AppPaths.LogDir, "linkroom.log");
        var logSink = new RollingLogSink(logFile, 500);
        var minLevel = saved.LogLevel?.ToLowerInvariant() switch
        {
            "debug" => LogLevel.Debug,
            "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            _ => LogLevel.Information,
        };

        using var loggerFactory = LoggerFactory.Create(b =>
        {
            b.AddProvider(logSink);
            b.SetMinimumLevel(minLevel);
        });

        var stunProvider = new StunServerProvider(loggerFactory.CreateLogger<StunServerProvider>());
        var natProbe = new NatProbeService(loggerFactory.CreateLogger<NatProbeService>(), stunProvider);
        var natDetector = new StunNatDetector(natProbe);
        var networkService = new NetworkInfoService(natDetector, loggerFactory.CreateLogger<NetworkInfoService>());
        var detectionCache = new DetectionCache(networkService, loggerFactory.CreateLogger<DetectionCache>());

        var processService = new EasyTierProcessService(
            Path.Combine(runtimeDir, "easytier-core.exe"),
            AppPaths.LogDir,
            loggerFactory.CreateLogger<EasyTierProcessService>());

        var guardian = new ProcessGuardian(processService, loggerFactory.CreateLogger<ProcessGuardian>());
        var updateService = new UpdateService(loggerFactory.CreateLogger<UpdateService>());

        var cliClient = new EasyTierCliClient(
            Path.Combine(runtimeDir, "easytier-cli.exe"),
            "127.0.0.1:15888",
            loggerFactory.CreateLogger<EasyTierCliClient>());

        var vm = new MainViewModel(
            new EasyTierConfigBuilder(loggerFactory.CreateLogger<EasyTierConfigBuilder>()),
            processService,
            cliClient,
            new ConnectionStateMachine(loggerFactory.CreateLogger<ConnectionStateMachine>()),
            new PathSelectionStrategy(loggerFactory.CreateLogger<PathSelectionStrategy>()),
            detectionCache,
            networkService,
            settingsService,
            new PeerPingService(loggerFactory.CreateLogger<PeerPingService>()),
            new WebPanelService(runtimeDir),
            new DiagnosticsService(settingsService),
            natProbe,
            stunProvider,
            updateService,
            guardian,
            loggerFactory.CreateLogger<MainViewModel>(),
            loggerFactory.CreateLogger<AutoReconnectService>());

        vm.RestoreSettings(saved);
        _ = stunProvider.RefreshRemoteListAsync();

        if (cli?.Headless == true && (cli.Create || cli.Join))
        {
            if (cli.LanMode) vm.UseLanMode = true;
            if (cli.SharedNode) vm.IsSharedNodeEnabled = true;
            if (cli.Create) await vm.CreateRoomCommand.ExecuteAsync(null);
            else if (cli.Join && cli.RoomId != null)
            {
                vm.RoomId = cli.RoomId;
                if (cli.Password != null) vm.Password = cli.Password;
                await vm.ConnectCommand.ExecuteAsync(null);
            }
            return;
        }

        var window = new MainWindow { DataContext = vm };
        vm.SetWindow(window);
        window.Show();

        if (!vm.FirstRunCompleted)
        {
            var wizard = new WizardWindow(vm) { Owner = window };
            wizard.ShowDialog();
        }

        if (vm.AutoCheckUpdate)
            _ = vm.CheckUpdateOnStartupAsync();

        if (cli?.Minimized == true) window.WindowState = WindowState.Minimized;
    }
}
