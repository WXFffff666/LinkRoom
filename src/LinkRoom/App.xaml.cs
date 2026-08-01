using System.Diagnostics;
using System.IO;
using System.Windows;
using LinkRoom.Core;
using LinkRoom.Gui;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom;

public partial class App : Application
{
    const string IssueBaseUrl = "https://github.com/WXFffff666/LinkRoom/issues/new";

    static int _handlingCrash;
    static bool? _ghAvailable;

    public static string Version { get; } =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.16.0";

    protected override async void OnStartup(StartupEventArgs e)
    {
        InstallCrashHooks();
        _ = Task.Run(DetectGhCli); // warm the gh check so the crash dialog does not block on it
        try
        {
            await StartupCoreAsync(e);
        }
        catch (Exception ex)
        {
            // async void — never let an exception escape OnStartup (BUG-10 fix).
            System.Diagnostics.Debug.WriteLine(ex);
            try { MessageBox.Show($"启动失败: {ex.Message}", "LinkRoom"); } catch { }
            Shutdown();
        }
    }

    /// <summary>
    /// Crash auto-diagnostics: every unhandled exception path exports a diagnostic
    /// zip, shows its path, and offers a pre-filled GitHub issue body.
    /// </summary>
    void InstallCrashHooks()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            HandleCrash(ev.ExceptionObject as Exception ?? new Exception("未知异常"));

        DispatcherUnhandledException += (_, ev) =>
        {
            HandleCrash(ev.Exception ?? new Exception("未知异常"));
            ev.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            HandleCrash(ev.Exception ?? new Exception("未知异常"));
            ev.SetObserved();
        };
    }

    static bool DetectGhCli()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("gh", "auth status")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    // Crash path: never re-throw — the diagnostics themselves must be fully
    // swallowed so a failure here can't cause a second crash (no recursion).
    void HandleCrash(Exception ex)
    {
        if (Interlocked.Exchange(ref _handlingCrash, 1) != 0) return;
        try
        {
            if (Dispatcher == null || Dispatcher.HasShutdownStarted) return;
            if (Dispatcher.CheckAccess()) HandleCrashOnUiThread(ex);
            else Dispatcher.Invoke(() => HandleCrashOnUiThread(ex));
        }
        catch { /* best effort — the process is already failing */ }
        finally { Interlocked.Exchange(ref _handlingCrash, 0); }
    }

    void HandleCrashOnUiThread(Exception ex)
    {
        string? zipPath = null;
        try
        {
            // Crash path: on AppDomain.UnhandledException the process dies right
            // after this handler returns, so the export must finish before the
            // dialog shows. Export is pure file/process IO with no UI-thread
            // dependency — the sync wait cannot deadlock.
#pragma warning disable VSTHRD002
            zipPath = Task.Run(() => new DiagnosticsService(new SettingsService()).ExportAsync(null))
                .GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
        catch { /* diagnostics export must never crash the crash handler */ }

        var gh = _ghAvailable ??= DetectGhCli();
        var body = DiagnosticsService.BuildIssueBody(zipPath, ex, Version, AppPaths.EasyTierVersion);

        var message = "LinkRoom 发生了未处理的异常，已自动导出诊断包：\n\n" +
                      $"{zipPath ?? "(诊断导出失败)"}\n\n" +
                      "提交 GitHub Issue 时可附上此诊断包（含日志、脱敏设置、系统信息）。\n\n" +
                      (gh
                          ? "是 = 一键创建 GitHub Issue（gh）\n否 = 复制 Issue 正文\n取消 = 关闭"
                          : "是 = 复制 Issue 正文到剪贴板\n否 = 打开 GitHub 手动提交\n取消 = 关闭");

        MessageBoxResult result;
        try { result = MessageBox.Show(message, "LinkRoom 崩溃报告", MessageBoxButton.YesNoCancel, MessageBoxImage.Error); }
        catch { return; }

        if (result == MessageBoxResult.Yes)
        {
            if (gh) RunGhIssueCreate(body, ex);
            else CopyIssueBody(body);
        }
        else if (result == MessageBoxResult.No)
        {
            if (gh) CopyIssueBody(body);
            else OpenIssuePage();
        }
    }

    static void CopyIssueBody(string body)
    {
        try
        {
            Clipboard.SetText(body);
            MessageBox.Show("Issue 正文已复制到剪贴板，请到 GitHub 新建 Issue 并粘贴。", "LinkRoom");
        }
        catch { }
    }

    static void OpenIssuePage()
    {
        try { Process.Start(new ProcessStartInfo(IssueBaseUrl) { UseShellExecute = true }); }
        catch { }
    }

    static void RunGhIssueCreate(string body, Exception ex)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"linkroom-issue-{Guid.NewGuid():N}.md");
        try
        {
            File.WriteAllText(tmp, body);
            var title = $"LinkRoom 崩溃报告: {ex.GetType().Name}";
            using var p = Process.Start(new ProcessStartInfo("gh", $"issue create --title \"{title}\" --body-file \"{tmp}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(30000);
            var url = output.Trim();
            if (p.ExitCode != 0 || string.IsNullOrEmpty(url))
                throw new Exception(string.IsNullOrEmpty(url) ? "gh 未返回 Issue 链接" : $"gh 退出码 {p.ExitCode}");
            try { Clipboard.SetText(url); } catch { }
            MessageBox.Show($"Issue 已创建：\n{url}\n\n链接已复制到剪贴板。", "GitHub Issue");
        }
        catch (Exception ghEx)
        {
            CopyIssueBody(body);
            MessageBox.Show($"gh 创建 Issue 失败：{ghEx.Message}\n\nIssue 正文已复制到剪贴板，请手动提交。", "GitHub Issue");
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    async Task StartupCoreAsync(StartupEventArgs e)
    {
        base.OnStartup(e);
        Exit += (_, _) => EasyTierProcessService.KillOrphanProcesses();

        // Services are manually wired in App.OnStartup (ServiceConfigurator removed as dead code)
        var cli = CliRunner.Parse(e.Args);
        var settingsService = new SettingsService();
        var saved = settingsService.Load();
        AppPaths.Configure(saved.PortableMode);
        AppPaths.EnsureDataDirectories();
        AppPaths.CleanupTempConfigs();
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
