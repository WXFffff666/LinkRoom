using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LinkRoom.Core;

/// <summary>
/// Persists and restores application settings to a JSON file.
/// Uses portable storage strategy: next-to-exe when writable, otherwise %LOCALAPPDATA%.
/// Password is NEVER persisted.
/// </summary>
public sealed class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Regex for sanitizing IP addresses in log lines
    private static readonly Regex IpMaskRegex = new(
        @"\b(\d{1,3}\.\d{1,3}\.\d{1,3})\.\d{1,3}\b",
        RegexOptions.Compiled);

    public SettingsService()
    {
        _settingsPath = GetSettingsPath();
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts)
                       ?? new AppSettings();
            }
        }
        catch { /* corrupt settings — start fresh */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_settingsPath);
        if (dir != null) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(_settingsPath, json);
    }

    /// <summary>Sanitize a log line by masking public IPs.</summary>
    public static string SanitizeLog(string line)
    {
        return IpMaskRegex.Replace(line, "$1.xxx");
    }

    private static string GetSettingsPath()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var portablePath = Path.Combine(exeDir, "config", "settings.json");

        // Try portable first
        try
        {
            var portableDir = Path.GetDirectoryName(portablePath);
            if (portableDir != null)
            {
                Directory.CreateDirectory(portableDir);
                // Test writability
                var testFile = Path.Combine(portableDir, ".write-test");
                File.WriteAllText(testFile, "");
                File.Delete(testFile);
                return portablePath;
            }
        }
        catch { /* fall through to local appdata */ }

        // Fallback to %LOCALAPPDATA%
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "LinkRoom", "config", "settings.json");
    }
}

/// <summary>
/// Application settings. Password is not persisted — user must re-enter each session.
/// </summary>
public sealed record AppSettings
{
    public string? LastRoomId { get; set; }
    public bool IsSharedNodeEnabled { get; set; }
    public string? SharedNodeUrls { get; set; }
    public string LogLevel { get; set; } = "Info";
    public bool IsUpnpDisabled { get; set; } = true;
    public string? CustomStunServers { get; set; }
    public int MaxReconnectAttempts { get; set; } = 5;
    public string? StaticVirtualIp { get; set; }
    public int ListenerPort { get; set; } = 11010;
}
