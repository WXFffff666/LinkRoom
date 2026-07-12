using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LinkRoom.Core;

/// <summary>
/// Persists and restores application settings. Password is NEVER persisted.
/// </summary>
public sealed class SettingsService
{
    readonly string _settingsPath;
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    static readonly Regex IpMaskRegex = new(
        @"\b(\d{1,3}\.\d{1,3}\.\d{1,3})\.\d{1,3}\b",
        RegexOptions.Compiled);

    static readonly Regex SecretRegex = new(
        @"(pass(?:word)?[=:\s]+)(\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SettingsService()
    {
        AppPaths.EnsureDataDirectories();
        _settingsPath = AppPaths.SettingsPath;
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
        }
        catch { /* corrupt */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOpts));
    }

    public void AddRoomHistory(string roomId)
    {
        var s = Load();
        s.RoomHistory ??= [];
        s.RoomHistory.RemoveAll(r => r.Equals(roomId, StringComparison.OrdinalIgnoreCase));
        s.RoomHistory.Insert(0, roomId);
        if (s.RoomHistory.Count > 5) s.RoomHistory = s.RoomHistory.Take(5).ToList();
        Save(s);
    }

    public static string SanitizeLog(string line)
    {
        line = SecretRegex.Replace(line, "$1[REDACTED]");
        return IpMaskRegex.Replace(line, "$1.xxx");
    }
}

public sealed record AppSettings
{
    public string? LastRoomId { get; set; }
    public List<string>? RoomHistory { get; set; }
    public bool IsSharedNodeEnabled { get; set; }
    public string? SharedNodeUrls { get; set; } = AppPaths.DefaultSharedNode;
    public string LogLevel { get; set; } = "Info";
    public bool IsUpnpDisabled { get; set; } = true;
    public bool AutoCheckUpdate { get; set; } = true;
    public string? SkippedUpdateVersion { get; set; }
    public bool FirstRunCompleted { get; set; }
    public bool Ipv6Only { get; set; }
    public bool EnableSocks5 { get; set; }
    public int Socks5Port { get; set; } = 1080;
    public bool RoomLocked { get; set; }
    public string? CustomStunServers { get; set; }
    public int MaxReconnectAttempts { get; set; } = 5;
    public string? StaticVirtualIp { get; set; }
    public int ListenerPort { get; set; } = 11010;
    public int Mtu { get; set; } = 1380;
    public bool PreferIPv6 { get; set; } = true;
    public bool PortableMode { get; set; } = true;
    public bool DarkMode { get; set; }
    /// <summary>LAN mode: TUN + UDP broadcast relay for game discovery.</summary>
    public bool UseLanMode { get; set; }
    public bool AutoStart { get; set; }
    public bool IsHostMode { get; set; } = true;
    public int? GamePortHint { get; set; }
}

public record AdvancedOptions
{
    public bool IsSharedNodeEnabled { get; init; }
    public string? SharedNodeUrls { get; init; }
    public string? LogLevel { get; init; } = "Info";
    public bool IsUpnpDisabled { get; init; } = true;
    public string? CustomStunServers { get; init; }
    public int MaxReconnectAttempts { get; init; } = 5;
    public string? StaticVirtualIp { get; init; }
    public int ListenerPort { get; init; } = 11010;
    public int Mtu { get; init; } = 1380;
    public bool PreferIPv6 { get; init; }
    public bool PortableMode { get; init; } = true;
    public bool UseLanMode { get; init; }
    public bool IsHostMode { get; init; } = true;
    public int? GamePortHint { get; init; }
    public bool Ipv6Only { get; init; }
    public bool EnableSocks5 { get; init; }
    public int Socks5Port { get; init; } = 1080;
    public bool RoomLocked { get; init; }
}

public record RoomOptions
{
    public string RoomId { get; init; } = "";
    public string Password { get; init; } = "";
}
