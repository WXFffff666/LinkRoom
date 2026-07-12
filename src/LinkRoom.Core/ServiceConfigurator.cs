using LinkRoom.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

public static class ServiceConfigurator
{
    public static IServiceProvider Build(string runtimeDir, AppSettings settings, ILoggerProvider logProvider)
    {
        var services = new ServiceCollection();

        services.AddSingleton(logProvider);
        services.AddLogging(b => b.AddProvider(logProvider));

        services.AddSingleton<SettingsService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<ProcessGuardian>();
        services.AddSingleton<DiagnosticsService>();
        services.AddSingleton<PeerPingService>();
        services.AddSingleton<WebPanelService>(_ => new WebPanelService(runtimeDir));
        services.AddSingleton<StunServerProvider>();
        services.AddSingleton<NatProbeService>();
        services.AddSingleton<StunNatDetector>();
        services.AddSingleton<NetworkInfoService>();
        services.AddSingleton<DetectionCache>();
        services.AddSingleton<EasyTierConfigBuilder>();
        services.AddSingleton<PathSelectionStrategy>();
        services.AddSingleton<ConnectionStateMachine>();
        services.AddSingleton(_ => new EasyTierProcessService(
            Path.Combine(runtimeDir, "easytier-core.exe"),
            AppPaths.LogDir,
            LoggerFactory.Create(b => b.AddProvider(logProvider)).CreateLogger<EasyTierProcessService>()));
        services.AddSingleton(_ => new EasyTierCliClient(
            Path.Combine(runtimeDir, "easytier-cli.exe"),
            "127.0.0.1:15888",
            LoggerFactory.Create(b => b.AddProvider(logProvider)).CreateLogger<EasyTierCliClient>()));

        return services.BuildServiceProvider();
    }
}
