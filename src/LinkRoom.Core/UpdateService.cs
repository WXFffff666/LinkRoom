using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LinkRoom.Core;

public sealed record UpdateInfo(
    string Tag,
    string SemVer,
    string DownloadUrl,
    long SizeBytes,
    string? ReleaseNotes,
    string? EasyTierVersion = null);

public sealed record UpdateCheckResult(bool HasUpdate, UpdateInfo? Info, string CurrentVersion);

public sealed record UpdateDownloadProgress(long Received, long Total, double Percent);

/// <summary>
/// Checks GitHub Releases, downloads updates incrementally (preserves LinkRoomData/runtime when EasyTier unchanged).
/// </summary>
public sealed class UpdateService
{
    const string Repo = "WXFffff666/LinkRoom";
    readonly ILogger<UpdateService> _log;
    readonly HttpClient _http;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _log = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LinkRoom", CurrentVersion));
    }

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.16.0";

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var current = CurrentVersion;
        try
        {
            var json = await _http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var semver = tag.TrimStart('v');
            if (!IsNewer(semver, current))
                return new UpdateCheckResult(false, null, current);

            string? url = null;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    if (name.Contains("LinkRoom", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        url = a.GetProperty("browser_download_url").GetString();
                        size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                        break;
                    }
                }
            }

            url ??= $"https://github.com/{Repo}/releases/latest/download/LinkRoom-{tag}-win-x64.exe";
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            var info = new UpdateInfo(tag, semver, url, size, notes, AppPaths.EasyTierVersion);
            return new UpdateCheckResult(true, info, current);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Update check failed");
            return new UpdateCheckResult(false, null, current);
        }
    }

    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var updateDir = Path.Combine(AppPaths.DataRoot, "update");
        Directory.CreateDirectory(updateDir);
        var dest = Path.Combine(updateDir, $"LinkRoom-{info.Tag}-win-x64.exe");
        var partial = dest + ".partial";

        using var resp = await _http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? info.SizeBytes;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        await using var fs = File.Create(partial);
        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            progress?.Report(new UpdateDownloadProgress(received, total, total > 0 ? received * 100.0 / total : 0));
        }

        if (File.Exists(dest)) File.Delete(dest);
        File.Move(partial, dest);

        var manifest = new UpdateManifest
        {
            AppVersion = info.SemVer,
            EasyTierVersion = info.EasyTierVersion ?? AppPaths.EasyTierVersion,
            DownloadedAt = DateTime.UtcNow,
            FilePath = dest,
        };
        await File.WriteAllTextAsync(Path.Combine(updateDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), ct);

        _log.LogInformation("Update downloaded: {Path} ({Size} bytes)", dest, received);
        return dest;
    }

    /// <summary>Incremental: only replaces exe; LinkRoomData/runtime preserved if EasyTier version matches.</summary>
    public bool IsIncrementalUpdate(UpdateInfo info)
    {
        var manifestPath = Path.Combine(AppPaths.DataRoot, "update", "manifest.json");
        if (!File.Exists(manifestPath)) return true;
        try
        {
            var old = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(manifestPath));
            return old?.EasyTierVersion == (info.EasyTierVersion ?? AppPaths.EasyTierVersion);
        }
        catch { return true; }
    }

    public void ApplyAndRestart(string newExePath)
    {
        var currentExe = Environment.ProcessPath!;
        var batch = Path.Combine(AppPaths.TempDir, "linkroom-apply-update.cmd");
        Directory.CreateDirectory(AppPaths.TempDir);
        File.WriteAllText(batch, $"""
            @echo off
            timeout /t 2 /nobreak >nul
            move /y "{currentExe}" "{currentExe}.bak" 2>nul
            copy /y "{newExePath}" "{currentExe}"
            start "" "{currentExe}"
            del "%~f0"
            """);
        Process.Start(new ProcessStartInfo(batch) { UseShellExecute = true, CreateNoWindow = true });
        Environment.Exit(0);
    }

    static bool IsNewer(string remote, string local)
    {
        if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
            return r > l;
        return !string.Equals(remote, local, StringComparison.OrdinalIgnoreCase);
    }

    sealed record UpdateManifest
    {
        public string? AppVersion { get; init; }
        public string? EasyTierVersion { get; init; }
        public DateTime DownloadedAt { get; init; }
        public string? FilePath { get; init; }
    }
}
