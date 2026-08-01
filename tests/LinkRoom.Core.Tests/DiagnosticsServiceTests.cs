using System.IO.Compression;
using LinkRoom.Network;

namespace LinkRoom.Core.Tests;

public class DiagnosticsServiceTests : IDisposable
{
    readonly string _root;
    readonly string _logDir;
    readonly DiagnosticsService _diag;

    public DiagnosticsServiceTests()
    {
        // Parameterized temp dir — keeps AppPaths static state out of the assertions.
        _root = Path.Combine(Path.GetTempPath(), "linkroom-diag-tests", Guid.NewGuid().ToString("N"));
        _logDir = Path.Combine(_root, "logs");
        var configDir = Path.Combine(_root, "config");
        Directory.CreateDirectory(_logDir);
        Directory.CreateDirectory(configDir);

        File.WriteAllText(Path.Combine(configDir, "settings.json"), """
            {
              "CustomStunServers": "stun:203.0.113.7:3478, stun:8.8.8.8",
              "Password": "hunter2secret",
              "StaticVirtualIp": "10.144.0.5"
            }
            """);

        _diag = new DiagnosticsService(new SettingsService(Path.Combine(configDir, "settings.json")), _root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task ExportAsync_ContainsAllFourCategories_AndAtMostThreeEasyTierLogs()
    {
        File.WriteAllText(Path.Combine(_logDir, "linkroom.log"), "linkroom rolling log\n");
        var e1 = Path.Combine(_logDir, "easytier-0001.log");
        var e2 = Path.Combine(_logDir, "easytier-0002.log");
        var e3 = Path.Combine(_logDir, "easytier-0003.log");
        var e4 = Path.Combine(_logDir, "easytier-0004.log");
        File.WriteAllText(e1, "oldest"); File.WriteAllText(e2, "older");
        File.WriteAllText(e3, "newer"); File.WriteAllText(e4, "newest");
        File.SetLastWriteTimeUtc(e1, DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(e2, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(e3, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(e4, DateTime.UtcNow);

        var zipPath = await _diag.ExportAsync(new NetworkSnapshot
        {
            NatType = NatType.FullCone,
            StunReachable = true,
            PublicIPv4 = "203.0.113.10",
            PublicIPv6 = "2001:db8::1",
            UdpReachable = true,
        });

        Assert.True(File.Exists(zipPath));
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        // Four manifest categories: settings / network / logs / system.
        Assert.Contains("settings.json", names);
        Assert.Contains("network.txt", names);
        Assert.Contains("system.txt", names);
        Assert.Contains("logs/linkroom.log", names);

        var easytier = names.Where(n => n.StartsWith("logs/easytier-")).ToList();
        Assert.Equal(3, easytier.Count);
        Assert.Contains("logs/easytier-0002.log", easytier);
        Assert.Contains("logs/easytier-0004.log", easytier);
        Assert.DoesNotContain("logs/easytier-0001.log", easytier);

        Assert.Contains("NAT=FullCone", ReadEntry(zip, "network.txt"));
    }

    [Fact]
    public async Task ExportAsync_NullSnapshot_StillWritesAllManifestEntries()
    {
        File.WriteAllText(Path.Combine(_logDir, "linkroom.log"), "x\n");

        var zipPath = await _diag.ExportAsync(null);

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("settings.json", names);
        Assert.Contains("network.txt", names);
        Assert.Contains("system.txt", names);
        Assert.Contains("logs/linkroom.log", names);
        Assert.Contains("snapshot=unavailable", ReadEntry(zip, "network.txt"));
    }

    [Fact]
    public async Task ExportAsync_SettingsSanitized_NoRawPublicIp_NoPasswordValue()
    {
        File.WriteAllText(Path.Combine(_logDir, "linkroom.log"), "x\n");

        var zipPath = await _diag.ExportAsync(null);

        using var zip = ZipFile.OpenRead(zipPath);
        var settings = ReadEntry(zip, "settings.json");

        Assert.DoesNotContain("203.0.113.7", settings);
        Assert.DoesNotContain("8.8.8.8", settings);
        Assert.DoesNotContain("hunter2secret", settings);
        Assert.Contains("203.0.113.xxx", settings);
        Assert.Contains("[REDACTED]", settings);
    }

    [Fact]
    public async Task ExportAsync_SystemInfo_HasOsArchDotnetAdminAndPorts()
    {
        File.WriteAllText(Path.Combine(_logDir, "linkroom.log"), "x\n");

        var zipPath = await _diag.ExportAsync(null);

        using var zip = ZipFile.OpenRead(zipPath);
        var system = ReadEntry(zip, "system.txt");
        Assert.Contains("OS=", system);
        Assert.Contains("Arch=", system);
        Assert.Contains(".NET=", system);
        Assert.Contains("Admin=", system);
        Assert.Contains("ListeningPorts=", system);
    }

    [Fact]
    public void BuildIssueBody_PrefillsAllTemplateSections()
    {
        var ex = new InvalidOperationException("boom");
        var body = DiagnosticsService.BuildIssueBody(
            @"C:\tmp\linkroom-diag-20260101-120000.zip", ex, "1.16.0", "2.6.4");

        Assert.Contains("## 发生了什么", body);
        Assert.Contains("InvalidOperationException: boom", body);
        Assert.Contains("## 诊断包", body);
        Assert.Contains("linkroom-diag-20260101-120000.zip", body);
        Assert.Contains("## 版本", body);
        Assert.Contains("- LinkRoom: 1.16.0", body);
        Assert.Contains("- EasyTier: 2.6.4", body);
    }

    static string ReadEntry(ZipArchive zip, string name)
    {
        var entry = Assert.Single(zip.Entries, e => e.FullName == name);
        using var r = new StreamReader(entry.Open());
        return r.ReadToEnd();
    }
}
