using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
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
///
/// Threat model (Metis m6): the .sha256 sidecar is downloaded from the same GitHub release as the exe,
/// so SHA256 verification only guards against corrupted downloads and transport-level tampering.
/// If the release source itself is compromised, the hash authenticates nothing — real authenticity
/// requires code-signing of releases (future work).
/// </summary>
public sealed class UpdateService
{
    const string Repo = "WXFffff666/LinkRoom";
    readonly ILogger<UpdateService> _log;
    readonly HttpClient _http;

    public UpdateService(ILogger<UpdateService> logger) : this(logger, CreateHttpClient()) { }

    internal UpdateService(ILogger<UpdateService> logger, HttpClient http)
    {
        _log = logger;
        _http = http;
    }

    static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LinkRoom", CurrentVersion));
        return http;
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
                    if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) continue; // checksum sidecar is not the exe
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

        long received;
        try
        {
            var buffer = new byte[81920];
            received = 0;
            int read;
            {
                // Nested scope: fs must be disposed before File.Move — an open FileShare.None
                // handle makes MoveFile fail on Windows (sharing violation, latent v1.16.0 bug).
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                await using var fs = File.Create(partial);
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    received += read;
                    progress?.Report(new UpdateDownloadProgress(received, total, total > 0 ? received * 100.0 / total : 0));
                }
            }

            if (File.Exists(dest)) File.Delete(dest);
            File.Move(partial, dest);
        }
        finally
        {
            // Never leave a stale .partial behind after a failed download (BUG-15).
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }
        }

        // SHA256 integrity gate: never proceed with an unverified exe (Metis m6).
        try
        {
            var expected = await FetchExpectedSha256Async(info, ct);
            var actual = await ComputeSha256Async(dest, ct);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"完整性校验失败: SHA256 不匹配 (期望 {expected}, 实际 {actual})");
        }
        catch
        {
            // Verification failure must block installation — remove the downloaded exe and any .partial.
            try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }
            throw;
        }

        var manifest = new UpdateManifest
        {
            AppVersion = info.SemVer,
            EasyTierVersion = info.EasyTierVersion ?? AppPaths.EasyTierVersion,
            DownloadedAt = DateTime.UtcNow,
            FilePath = dest,
        };
        await File.WriteAllTextAsync(Path.Combine(updateDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), ct);

        _log.LogInformation("Update downloaded & SHA256 verified: {Path} ({Size} bytes)", dest, received);
        return dest;
    }

    /// <summary>Downloads the sibling .sha256 sidecar and extracts the expected hex hash (no fallback on failure).</summary>
    async Task<string> FetchExpectedSha256Async(UpdateInfo info, CancellationToken ct)
    {
        var shaUrl = info.DownloadUrl + ".sha256";
        string text;
        try
        {
            text = await _http.GetStringAsync(shaUrl, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"完整性校验失败: 无法获取 SHA256 校验文件 {shaUrl} ({ex.Message})", ex);
        }
        var expected = ParseSha256(text);
        if (expected == null)
        {
            var preview = text.Trim();
            if (preview.Length > 120) preview = preview[..120];
            throw new InvalidOperationException($"完整性校验失败: .sha256 文件格式非法 (未找到 64 位十六进制哈希): {preview}");
        }
        return expected;
    }

    /// <summary>
    /// GitHub .sha256 sidecars look like "&lt;hash&gt;  &lt;filename&gt;" (two spaces) or
    /// "&lt;hash&gt; *&lt;filename&gt;" (binary marker) — take the first 64-hex token.
    /// </summary>
    static string? ParseSha256(string text)
    {
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (token.Length == 64 && token.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
                return token;
        return null;
    }

    static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(fs, ct));
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
