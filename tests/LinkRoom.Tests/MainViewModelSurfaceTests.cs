using System.Reflection;
using LinkRoom.Core;
using LinkRoom.Gui;
using LinkRoom.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinkRoom.Tests;

/// <summary>
/// Feature-fixing (characterization) tests for MainViewModel's public surface.
///
/// These tests exist to lock the VM's observable surface BEFORE the god-class
/// split refactor (pure move, zero logic changes). They assert:
///  1. Every {Binding Xxx} property/command referenced by src/LinkRoom/*.xaml
///     still exists as a public member of MainViewModel (XAML bindings are
///     stringly-typed — a renamed property silently renders nothing).
///  2. SaveSettingsNow() → SettingsService.Load() → RestoreSettings() round-trip
///     preserves every persisted property.
///
/// They deliberately avoid anything that touches processes, network, the
/// clipboard, MessageBox or the WPF Dispatcher (Application.Current is null
/// under xunit, so Ui()/UiAsync() are safe no-ops).
/// </summary>
public class MainViewModelSurfaceTests
{
    // Verbatim list extracted from src/LinkRoom/{MainWindow,SettingsWindow}.xaml
    // {Binding Xxx} occurrences (source of truth: the XAML files, not this VM).
    static readonly string[] XamlBoundProperties =
    [
        "AutoCheckUpdate", "AutoStart", "ConnectionQuality", "CustomStunServers",
        "DarkMode", "EnableSocks5", "Ipv4", "Ipv6Only", "IsHostMode", "IsProgressVisible",
        "IsSharedNodeEnabled", "IsUpnpDisabled", "ListenerPort", "MaxReconnectAttempts",
        "Mtu", "NatType", "PasswordStrengthHint", "PathDiagram", "Peers", "PortableMode",
        "PortForwardHint", "PreferIPv6", "ProgressText", "ProgressValue", "RoomHistory",
        "RoomId", "RoomLocked", "SharedNodeUrls", "ShortLinkText", "Socks5Port",
        "StaticVirtualIp", "StatusDetail", "StatusText", "UseLanMode", "VirtualIpv4",
        "VirtualIpv6",
    ];

    static readonly string[] XamlBoundCommands =
    [
        "ConnectCommand", "CopyLinkCodeCommand", "CreateRoomCommand",
        "DisconnectCommand", "SpeedTestCommand",
    ];

    [Fact]
    public void XamlBoundProperties_AllExistOnMainViewModel()
    {
        var properties = typeof(MainViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        var missing = XamlBoundProperties.Where(name => !properties.Contains(name)).ToList();
        Assert.True(missing.Count == 0,
            $"XAML-bound properties missing from MainViewModel: {string.Join(", ", missing)}");
    }

    [Fact]
    public void XamlBoundCommands_AllExistOnMainViewModel()
    {
        var properties = typeof(MainViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        var missing = XamlBoundCommands.Where(name => !properties.Contains(name)).ToList();
        Assert.True(missing.Count == 0,
            $"XAML-bound commands missing from MainViewModel: {string.Join(", ", missing)}");
    }

    [Fact]
    public void PublicNonCommandSurface_IsStable()
    {
        // The full public member surface that code outside the VM relies on
        // (App.xaml.cs, MainWindow.xaml.cs, Wizard). Locking it catches any
        // accidental rename/removal during the split.
        string[] expectedPublicMembers =
        [
            // commands (RelayCommand-generated)
            "ConnectCommand", "CopyLinkCodeCommand", "CreateRoomCommand", "DisconnectCommand",
            "SpeedTestCommand", "PingPeersCommand", "ExportDiagnosticsCommand", "OpenWebPanelCommand",
            "CopyLinkCodeCommand", "RefreshStunListCommand", "JoinHistoryRoomCommand",
            "CheckUpdateManualCommand", "CopyVirtualIpCommand", "ExportConfigCommand",
            "ImportConfigCommand", "ScanModsCommand", "CheckEasyTierVersionCommand", "RefreshNetworkCommand",
            // methods
            "SetWindow", "RestoreSettings", "L", "SaveSettingsNow", "CheckUpdateOnStartupAsync",
            "RunNatTestAsync", "RunSelfCheck",
            // static surface
            "LogLines",
        ];

        var actual = typeof(MainViewModel)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet();

        var missing = expectedPublicMembers.Where(name => !actual.Contains(name)).ToList();
        Assert.True(missing.Count == 0,
            $"Public members missing from MainViewModel: {string.Join(", ", missing)}");
    }

    [Fact]
    public void SaveRestore_RoundTrip_PreservesAllPersistedProperties()
    {
        var dir = Path.Combine(Path.GetTempPath(), "linkroom-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var vm = BuildVm(dir);
            vm.RoomId = "ABC12345";
            vm.Password = "s3cret!";
            vm.DarkMode = true;
            vm.PreferIPv6 = false;
            vm.Mtu = 1400;
            vm.ListenerPort = 12000;
            vm.MaxReconnectAttempts = 9;
            vm.CustomStunServers = "stun1.example.com:3478";
            vm.StaticVirtualIp = "10.26.0.100";
            vm.Ipv6Only = true;
            vm.EnableSocks5 = true;
            vm.Socks5Port = 11080;
            vm.RoomLocked = true;
            vm.UseLanMode = true;
            vm.IsHostMode = false;
            vm.GamePortHint = 25565;
            vm.SkippedUpdateVersion = "v9.9.9";
            vm.FirstRunCompleted = true;
            vm.AutoCheckUpdate = false;
            vm.IsUpnpDisabled = false;
            vm.SharedNodeUrls = "tcp://relay.example.com:11010";
            vm.LogLevel = "Debug";
            vm.PortableMode = true; // keep true so AppPaths stays on the exe dir
            vm.AutoStart = AutoStartService.IsEnabled(); // system truth: RestoreSettings won't touch the registry
            vm.RoomHistory.Add("OLDROOM1");
            vm.RoomHistory.Add("ABC12345");

            vm.SaveSettingsNow();

            // 1) The persisted file matches what we set.
            var settings = new SettingsService(Path.Combine(dir, "settings.json")).Load();
            Assert.Equal("ABC12345", settings.LastRoomId);
            Assert.True(settings.DarkMode);
            Assert.False(settings.PreferIPv6);
            Assert.Equal(1400, settings.Mtu);
            Assert.Equal(12000, settings.ListenerPort);
            Assert.Equal(9, settings.MaxReconnectAttempts);
            Assert.Equal("stun1.example.com:3478", settings.CustomStunServers);
            Assert.Equal("10.26.0.100", settings.StaticVirtualIp);
            Assert.True(settings.Ipv6Only);
            Assert.True(settings.EnableSocks5);
            Assert.Equal(11080, settings.Socks5Port);
            Assert.True(settings.RoomLocked);
            Assert.True(settings.UseLanMode);
            Assert.False(settings.IsHostMode);
            Assert.Equal(25565, settings.GamePortHint);
            Assert.Equal("v9.9.9", settings.SkippedUpdateVersion);
            Assert.True(settings.FirstRunCompleted);
            Assert.False(settings.AutoCheckUpdate);
            Assert.False(settings.IsUpnpDisabled);
            Assert.Equal("tcp://relay.example.com:11010", settings.SharedNodeUrls);
            Assert.Equal("Debug", settings.LogLevel);
            Assert.True(settings.PortableMode);
            Assert.Equal(2, settings.RoomHistory?.Count);
            Assert.Equal("OLDROOM1", settings.RoomHistory?[0]);
            Assert.Equal("ABC12345", settings.RoomHistory?[1]);

            // 2) A fresh VM restores those exact values from the settings.
            var vm2 = BuildVm(dir);
            vm2.RestoreSettings(settings);

            Assert.Equal("ABC12345", vm2.RoomId);
            Assert.True(vm2.DarkMode);
            Assert.False(vm2.PreferIPv6);
            Assert.Equal(1400, vm2.Mtu);
            Assert.Equal(12000, vm2.ListenerPort);
            Assert.Equal(9, vm2.MaxReconnectAttempts);
            Assert.Equal("stun1.example.com:3478", vm2.CustomStunServers);
            Assert.Equal("10.26.0.100", vm2.StaticVirtualIp);
            Assert.True(vm2.Ipv6Only);
            Assert.True(vm2.EnableSocks5);
            Assert.Equal(11080, vm2.Socks5Port);
            Assert.True(vm2.RoomLocked);
            Assert.True(vm2.UseLanMode);
            Assert.False(vm2.IsHostMode);
            Assert.Equal(25565, vm2.GamePortHint);
            Assert.Equal("v9.9.9", vm2.SkippedUpdateVersion);
            Assert.True(vm2.FirstRunCompleted);
            Assert.False(vm2.AutoCheckUpdate);
            Assert.False(vm2.IsUpnpDisabled);
            Assert.Equal("tcp://relay.example.com:11010", vm2.SharedNodeUrls);
            Assert.Equal("Debug", vm2.LogLevel);
            Assert.True(vm2.PortableMode);
            Assert.Equal(2, vm2.RoomHistory.Count);
            Assert.Equal("OLDROOM1", vm2.RoomHistory[0]);
            Assert.Equal("ABC12345", vm2.RoomHistory[1]);

            // Password is NEVER persisted (AppSettings has no Password field),
            // so a restored VM must not leak it — the hint stays empty.
            Assert.Equal("", vm2.PasswordStrengthHint);
            Assert.Equal("", vm2.Password);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RestoreSettings_AppliesDefaults_WhenValuesInvalidOrMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "linkroom-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var vm = BuildVm(dir);
            vm.RestoreSettings(new AppSettings
            {
                // defaults/fallbacks exercised: Mtu out of range, port 0, attempts 0
                Mtu = 9999,
                ListenerPort = 0,
                MaxReconnectAttempts = 0,
                SharedNodeUrls = "   ",
                AutoStart = AutoStartService.IsEnabled(),
            });

            Assert.Equal(1380, vm.Mtu);
            Assert.Equal(11010, vm.ListenerPort);
            Assert.Equal(5, vm.MaxReconnectAttempts);
            Assert.Equal(AppPaths.DefaultSharedNode, vm.SharedNodeUrls);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    static MainViewModel BuildVm(string dir)
    {
        var logger = NullLogger<MainViewModel>.Instance;
        var settings = new SettingsService(Path.Combine(dir, "settings.json"));
        var stun = new StunServerProvider(NullLogger<StunServerProvider>.Instance);
        var natProbe = new NatProbeService(NullLogger<NatProbeService>.Instance, stun);
        var network = new NetworkInfoService(new StunNatDetector(natProbe), NullLogger<NetworkInfoService>.Instance);
        var proc = new EasyTierProcessService(Path.Combine(dir, "easytier-core.exe"), dir, NullLogger<EasyTierProcessService>.Instance);
        var cli = new EasyTierCliClient(Path.Combine(dir, "easytier-cli.exe"), "127.0.0.1:15888", NullLogger<EasyTierCliClient>.Instance);
        var guardian = new ProcessGuardian(proc, NullLogger<ProcessGuardian>.Instance);

        return new MainViewModel(
            new EasyTierConfigBuilder(NullLogger<EasyTierConfigBuilder>.Instance),
            proc,
            cli,
            new ConnectionStateMachine(NullLogger<ConnectionStateMachine>.Instance),
            new PathSelectionStrategy(NullLogger<PathSelectionStrategy>.Instance),
            new DetectionCache(network, NullLogger<DetectionCache>.Instance),
            network,
            settings,
            new PeerPingService(NullLogger<PeerPingService>.Instance),
            new WebPanelService(dir),
            new DiagnosticsService(settings),
            natProbe,
            stun,
            new UpdateService(NullLogger<UpdateService>.Instance),
            guardian,
            logger,
            NullLogger<AutoReconnectService>.Instance);
    }
}
