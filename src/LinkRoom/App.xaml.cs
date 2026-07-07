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

        // Build minimal service graph (no DI container — direct construction)
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var stateMachine = new ConnectionStateMachine(loggerFactory.CreateLogger<ConnectionStateMachine>());
        var configBuilder = new EasyTierConfigBuilder(loggerFactory.CreateLogger<EasyTierConfigBuilder>());

        // EasyTier paths — will be set after runtime extraction
        var easytierCorePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkRoom", "runtime", "2.6.4", "easytier-core.exe");
        var easytierCliPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkRoom", "runtime", "2.6.4", "easytier-cli.exe");
        var logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        var processService = new EasyTierProcessService(easytierCorePath, logDir,
            loggerFactory.CreateLogger<EasyTierProcessService>());
        var cliClient = new EasyTierCliClient(easytierCliPath, "127.0.0.1:15888",
            loggerFactory.CreateLogger<EasyTierCliClient>());
        var pathSelector = new PathSelectionStrategy(loggerFactory.CreateLogger<PathSelectionStrategy>());

        var viewModel = new MainViewModel(
            configBuilder, processService, cliClient,
            stateMachine, pathSelector,
            loggerFactory.CreateLogger<MainViewModel>());

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
    }
}