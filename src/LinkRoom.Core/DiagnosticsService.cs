using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using LinkRoom.Network;

namespace LinkRoom.Core;

/// <summary>
/// Exports a diagnostic zip: sanitized settings, logs (linkroom rolling log +
/// up to 3 newest easytier logs), system info, and network snapshot.
/// Used both by the manual export button and the crash auto-diagnostics path.
/// </summary>
public sealed class DiagnosticsService
{
    readonly SettingsService _settings;
    readonly string? _dataRoot;

    public DiagnosticsService(SettingsService settings, string? dataRoot = null)
    {
        _settings = settings;
        _dataRoot = dataRoot;
    }

    // Optional test override; production resolves through AppPaths.
    string LogDir => _dataRoot == null ? AppPaths.LogDir : Path.Combine(_dataRoot, "logs");
    string DiagnosticsDir => _dataRoot == null ? AppPaths.DiagnosticsDir : Path.Combine(_dataRoot, "diagnostics");

    public async Task<string> ExportAsync(NetworkSnapshot? snapshot, CancellationToken ct = default)
    {
        AppPaths.EnsureDataDirectories();
        Directory.CreateDirectory(DiagnosticsDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var zipPath = Path.Combine(DiagnosticsDir, $"linkroom-diag-{stamp}.zip");

        await using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        AddText(zip, "settings.json", ReadSanitizedSettings());
        AddText(zip, "paths.txt", $"DataRoot={AppPaths.DataRoot}\nRuntime={AppPaths.RuntimeDir}\nPortable={AppPaths.IsPortable}");
        AddText(zip, "network.txt", snapshot == null
            ? "NAT=unknown\nIPv4=unknown\nIPv6=unknown\nUDP=unknown\nsnapshot=unavailable(crash)"
            : $"NAT={snapshot.NatType}\nIPv4={snapshot.PublicIPv4}\nIPv6={snapshot.PublicIPv6}\nUDP={snapshot.UdpReachable}");
        AddText(zip, "system.txt", await BuildSystemInfoAsync());

        var linkroomLog = Path.Combine(LogDir, "linkroom.log");
        var easytierLogs = Directory.GetFiles(LogDir, "easytier-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(3);
        foreach (var log in new[] { linkroomLog }.Concat(easytierLogs))
        {
            if (!File.Exists(log)) continue;
            var entry = zip.CreateEntry($"logs/{Path.GetFileName(log)}");
            await using var es = entry.Open();
            await using var ls = File.OpenRead(log);
            await ls.CopyToAsync(es, ct);
        }

        return zipPath;
    }

    /// <summary>Settings.json redacted at SettingsService.SanitizeLog level (IP mask + secret redaction).</summary>
    string ReadSanitizedSettings()
    {
        try
        {
            if (File.Exists(_settings.SettingsPath))
            {
                var lines = File.ReadAllLines(_settings.SettingsPath);
                return string.Join(Environment.NewLine, lines.Select(SettingsService.SanitizeLog));
            }
        }
        catch { /* fall through to model serialization */ }
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(_settings.Load(),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch { return "{}"; }
    }

    static async Task<string> BuildSystemInfoAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"OS={Environment.OSVersion.VersionString}");
        sb.AppendLine($"Arch={RuntimeInformation.OSArchitecture}");
        sb.AppendLine($".NET={RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Admin={AdminHelper.IsAdministrator()}");
        sb.AppendLine($"ListeningPorts={await ListeningPortSummaryAsync()}");
        return sb.ToString();
    }

    /// <summary>Summarizes LISTENING TCP ports (port numbers only — no addresses).</summary>
    static async Task<string> ListeningPortSummaryAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("netstat", "-ano")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return "unavailable";
            var outputTask = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } }
            var output = await outputTask;

            var ports = new SortedSet<string>();
            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var local = parts[1];
                var colon = local.LastIndexOf(':');
                if (colon < 0) continue;
                ports.Add($"{local[(colon + 1)..]}/tcp");
            }
            return ports.Count == 0 ? "none" : $"{ports.Count} listening: {string.Join(",", ports.Take(50))}";
        }
        catch { return "unavailable"; }
    }

    /// <summary>Prefilled GitHub issue body for crash reports.</summary>
    public static string BuildIssueBody(string? zipPath, Exception? ex, string appVersion, string easyTierVersion)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 发生了什么");
        sb.AppendLine();
        if (ex != null)
        {
            sb.AppendLine(SettingsService.SanitizeLog($"{ex.GetType().Name}: {ex.Message}"));
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                sb.AppendLine("```");
                sb.AppendLine(string.Join(Environment.NewLine,
                    SettingsService.SanitizeLog(ex.StackTrace).Split('\n').Take(20)));
                sb.AppendLine("```");
            }
        }
        else
        {
            sb.AppendLine("LinkRoom 发生了未处理的异常。");
        }
        sb.AppendLine();
        sb.AppendLine("## 诊断包");
        sb.AppendLine();
        sb.AppendLine($"提交前请将诊断包上传到本 Issue（拖拽附件）：`{zipPath ?? "(导出失败，请手动提供日志)"}`");
        sb.AppendLine();
        sb.AppendLine("## 版本");
        sb.AppendLine();
        sb.AppendLine($"- LinkRoom: {appVersion}");
        sb.AppendLine($"- EasyTier: {easyTierVersion}");
        sb.AppendLine($"- OS: {Environment.OSVersion}");
        return sb.ToString();
    }

    static void AddText(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name);
        using var w = new StreamWriter(e.Open());
        w.Write(content);
    }
}
