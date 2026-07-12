using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LinkRoom.Core;

public static class ConfigImportExportService
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static string Export(AppSettings settings)
    {
        var export = settings with { };
        return JsonSerializer.Serialize(export, Opts);
    }

    public static AppSettings Import(string json)
    {
        return JsonSerializer.Deserialize<AppSettings>(json, Opts) ?? new AppSettings();
    }

    public static async Task ExportToFileAsync(AppSettings settings, string? path = null)
    {
        path ??= Path.Combine(AppPaths.ConfigDir, $"linkroom-export-{DateTime.Now:yyyyMMdd-HHmmss}.linkroom.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, Export(settings), Encoding.UTF8);
    }

    public static async Task<AppSettings> ImportFromFileAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return Import(json);
    }
}

public static class ShortLinkService
{
    public static string ToShortCode(string roomId, string? password)
    {
        var input = $"{roomId}:{password ?? ""}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    public static string FormatShare(string roomId, string? password, int? port)
    {
        var code = ToShortCode(roomId, password);
        var link = LinkCodeService.Encode(roomId, password, port);
        return $"短码: {code}\n完整链接:\n{link}";
    }
}

public static class PathVisualizationService
{
    public static string Build(string? natType, string strategy, string connType, bool isRelay, bool upnp)
    {
        var sb = new StringBuilder();
        sb.AppendLine("┌─────────┐");
        sb.AppendLine($"│ NAT: {natType ?? "?"} │");
        sb.AppendLine("└────┬────┘");
        sb.AppendLine("     ▼");
        sb.AppendLine($"  [{strategy}]");
        if (upnp) sb.AppendLine("     │ UPnP ✓");
        sb.AppendLine("     ▼");
        sb.AppendLine(isRelay || connType.Contains("relay", StringComparison.OrdinalIgnoreCase)
            ? "  ⚡ 中继节点 ──► 对端"
            : "  ✅ P2P 直连 ──► 对端");
        return sb.ToString().TrimEnd();
    }
}

public static class ModDetectorService
{
    static readonly string[] ModPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "mods"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "versions"),
    ];

    public static ModScanResult ScanMinecraft()
    {
        var mods = new List<string>();
        foreach (var p in ModPaths)
        {
            if (!Directory.Exists(p)) continue;
            try
            {
                mods.AddRange(Directory.GetFiles(p, "*.jar", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName).OfType<string>());
            }
            catch { }
        }
        return new ModScanResult(mods.Count, mods.Take(20).ToList());
    }
}

public record ModScanResult(int TotalCount, IReadOnlyList<string> SampleNames);

public static class SpeedTestService
{
    public static async Task<(bool Ok, long Ms, string Detail)> TestTcpAsync(string host, int port, CancellationToken ct = default)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await client.ConnectAsync(host, port, ct);
            sw.Stop();
            return (true, sw.ElapsedMilliseconds, $"TCP {host}:{port} {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            return (false, -1, ex.Message);
        }
    }
}

public static class EasyTierUpdateService
{
    public static string EmbeddedVersion => AppPaths.EasyTierVersion;

    public static async Task<string?> CheckLatestEasyTierVersionAsync(CancellationToken ct = default)
    {
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            hc.DefaultRequestHeaders.UserAgent.ParseAdd("LinkRoom");
            var json = await hc.GetStringAsync("https://api.github.com/repos/EasyTier/EasyTier/releases/latest", ct);
            return JsonDocument.Parse(json).RootElement.GetProperty("tag_name").GetString();
        }
        catch { return null; }
    }
}
