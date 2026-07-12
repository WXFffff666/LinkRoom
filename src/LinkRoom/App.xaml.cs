using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using LinkRoom.Core;
using LinkRoom.Gui;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom;

public partial class App : Application
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.15.0";

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
            loggerFactory.CreateLogger<MainViewModel>(),
            loggerFactory.CreateLogger<AutoReconnectService>());

        vm.RestoreSettings(saved);
        _ = stunProvider.RefreshRemoteListAsync();
        _ = CheckForUpdatesAsync();

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
        if (cli?.Minimized == true) window.WindowState = WindowState.Minimized;
    }

    static async Task CheckForUpdatesAsync()
    {
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            hc.DefaultRequestHeaders.UserAgent.ParseAdd("LinkRoom");
            var json = await hc.GetStringAsync("https://api.github.com/repos/WXFffff666/LinkRoom/releases/latest");
            var tag = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("tag_name").GetString();
            var current = "v" + Version;
            if (tag != null && tag != current && Current != null)
            {
                await Current.Dispatcher.InvokeAsync(() =>
                {
                    if (MessageBox.Show($"发现新版本 {tag}\n当前: {current}\n\n打开下载？", "LinkRoom",
                            MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = $"https://github.com/WXFffff666/LinkRoom/releases/tag/{tag}",
                            UseShellExecute = true
                        });
                });
            }
        }
        catch { }
    }
}
