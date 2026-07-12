using System.IO.Compression;
using LinkRoom.Network;

namespace LinkRoom.Core;

public sealed class DiagnosticsService
{
    readonly SettingsService _settings;

    public DiagnosticsService(SettingsService settings) => _settings = settings;

    public async Task<string> ExportAsync(NetworkSnapshot? snapshot, CancellationToken ct = default)
    {
        AppPaths.EnsureDataDirectories();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var zipPath = Path.Combine(AppPaths.DiagnosticsDir, $"linkroom-diag-{stamp}.zip");

        await using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        AddText(zip, "settings.json", System.Text.Json.JsonSerializer.Serialize(_settings.Load(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        AddText(zip, "paths.txt", $"DataRoot={AppPaths.DataRoot}\nRuntime={AppPaths.RuntimeDir}\nPortable={AppPaths.IsPortable}");

        if (snapshot != null)
            AddText(zip, "network.txt", $"NAT={snapshot.NatType}\nIPv4={snapshot.PublicIPv4}\nIPv6={snapshot.PublicIPv6}\nUDP={snapshot.UdpReachable}");

        foreach (var log in Directory.GetFiles(AppPaths.LogDir, "*.log").TakeLast(5))
        {
            var entry = zip.CreateEntry($"logs/{Path.GetFileName(log)}");
            await using var es = entry.Open();
            await using var ls = File.OpenRead(log);
            await ls.CopyToAsync(es, ct);
        }

        return zipPath;
    }

    static void AddText(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name);
        using var w = new StreamWriter(e.Open());
        w.Write(content);
    }
}
