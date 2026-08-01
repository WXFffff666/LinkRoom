using System.Collections.Generic;
using LinkRoom.Core;
using LinkRoom.Network;

namespace LinkRoom.Gui;

/// <summary>
/// Owns the settings mapping between the VM's observable properties and the
/// persisted AppSettings: SaveSettings / RestoreSettings / AdvancedOptions
/// assembly and immediate-save. Pure move out of MainViewModel — no logic
/// changes. Shared VM state is read/written through the MainViewModel reference.
/// </summary>
internal sealed class SettingsFacade
{
    readonly MainViewModel _vm;
    readonly SettingsService _ss;
    readonly NetworkInfoService _ns;

    public SettingsFacade(MainViewModel vm, SettingsService ss, NetworkInfoService ns)
    {
        _vm = vm; _ss = ss; _ns = ns;
    }

    public void RestoreSettings(AppSettings s)
    {
        AppPaths.Configure(s.PortableMode);
        if (!string.IsNullOrEmpty(s.LastRoomId)) _vm.RoomId = s.LastRoomId;
        _vm.IsSharedNodeEnabled = s.IsSharedNodeEnabled;
        _vm.SharedNodeUrls = string.IsNullOrWhiteSpace(s.SharedNodeUrls) ? AppPaths.DefaultSharedNode : s.SharedNodeUrls;
        _vm.EnableSecureMode = s.EnableSecureMode;
        _vm.SharedNodePublicKey = s.SharedNodePublicKey ?? "";
        _vm.LogLevel = s.LogLevel ?? "Info";
        _vm.CustomStunServers = s.CustomStunServers ?? "";
        _vm.StaticVirtualIp = s.StaticVirtualIp ?? "";
        _vm.MaxReconnectAttempts = s.MaxReconnectAttempts > 0 ? s.MaxReconnectAttempts : 5;
        _vm.ListenerPort = s.ListenerPort > 0 ? s.ListenerPort : 11010;
        _vm.Mtu = s.Mtu is >= 576 and <= 1500 ? s.Mtu : 1380;
        _vm.PreferIPv6 = s.PreferIPv6;
        _vm.PortableMode = s.PortableMode;
        _vm.DarkMode = s.DarkMode;
        _vm.UseLanMode = s.UseLanMode;
        _vm.IsHostMode = s.IsHostMode;
        _vm.Language = s.Language ?? "system";
        _vm.AutoStart = s.AutoStart;
        _vm.GamePortHint = s.GamePortHint;
        _vm.IsUpnpDisabled = s.IsUpnpDisabled;
        _vm.AutoCheckUpdate = s.AutoCheckUpdate;
        _vm.SkippedUpdateVersion = s.SkippedUpdateVersion;
        _vm.FirstRunCompleted = s.FirstRunCompleted;
        _vm.Ipv6Only = s.Ipv6Only;
        _vm.EnableSocks5 = s.EnableSocks5;
        _vm.Socks5Port = s.Socks5Port > 0 ? s.Socks5Port : 1080;
        _vm.RoomLocked = s.RoomLocked;
        _vm.ReconnectService.MaxAttempts = _vm.MaxReconnectAttempts;
        _ns.SetCustomStunServers(_vm.CustomStunServers);

        _vm.RoomHistory.Clear();
        foreach (var r in s.RoomHistory ?? []) _vm.RoomHistory.Add(r);

        if (_vm.AutoStart != AutoStartService.IsEnabled())
            AutoStartService.SetEnabled(_vm.AutoStart);
    }

    public AdvancedOptions Adv() => new()
    {
        IsSharedNodeEnabled = _vm.IsSharedNodeEnabled,
        SharedNodeUrls = _vm.SharedNodeUrls,
        EnableSecureMode = _vm.EnableSecureMode,
        SharedNodePublicKey = string.IsNullOrWhiteSpace(_vm.SharedNodePublicKey) ? null : _vm.SharedNodePublicKey.Trim(),
        LogLevel = _vm.LogLevel,
        IsUpnpDisabled = _vm.IsUpnpDisabled,
        CustomStunServers = _vm.CustomStunServers,
        MaxReconnectAttempts = _vm.MaxReconnectAttempts,
        StaticVirtualIp = _vm.StaticVirtualIp,
        ListenerPort = _vm.ListenerPort,
        Mtu = _vm.Mtu,
        PreferIPv6 = _vm.PreferIPv6,
        PortableMode = _vm.PortableMode,
        UseLanMode = _vm.UseLanMode,
        IsHostMode = _vm.IsHostMode,
        GamePortHint = _vm.GamePortHint,
        Ipv6Only = _vm.Ipv6Only,
        EnableSocks5 = _vm.EnableSocks5,
        Socks5Port = _vm.Socks5Port,
        RoomLocked = _vm.RoomLocked,
    };

    public AppSettings SaveSettings() => new()
    {
        LastRoomId = _vm.RoomId.Trim(),
        RoomHistory = _vm.RoomHistory.ToList(),
        IsSharedNodeEnabled = _vm.IsSharedNodeEnabled,
        SharedNodeUrls = _vm.SharedNodeUrls,
        EnableSecureMode = _vm.EnableSecureMode,
        SharedNodePublicKey = string.IsNullOrWhiteSpace(_vm.SharedNodePublicKey) ? null : _vm.SharedNodePublicKey.Trim(),
        LogLevel = _vm.LogLevel,
        CustomStunServers = _vm.CustomStunServers,
        MaxReconnectAttempts = _vm.MaxReconnectAttempts,
        StaticVirtualIp = _vm.StaticVirtualIp,
        ListenerPort = _vm.ListenerPort,
        Mtu = _vm.Mtu,
        PreferIPv6 = _vm.PreferIPv6,
        PortableMode = _vm.PortableMode,
        DarkMode = _vm.DarkMode,
        UseLanMode = _vm.UseLanMode,
        IsHostMode = _vm.IsHostMode,
        Language = _vm.Language,
        AutoStart = _vm.AutoStart,
        GamePortHint = _vm.GamePortHint,
        IsUpnpDisabled = _vm.IsUpnpDisabled,
        AutoCheckUpdate = _vm.AutoCheckUpdate,
        SkippedUpdateVersion = _vm.SkippedUpdateVersion,
        FirstRunCompleted = _vm.FirstRunCompleted,
        Ipv6Only = _vm.Ipv6Only,
        EnableSocks5 = _vm.EnableSocks5,
        Socks5Port = _vm.Socks5Port,
        RoomLocked = _vm.RoomLocked,
        // Persist the room → lock secret map so the host re-uses the same
        // group_secret on reconnects. Keep any prior entries (other rooms)
        // and only refresh the entry for the current room.
        RoomLockSecrets = _vm.LockSecret is null
            ? _ss.Load().RoomLockSecrets
            : MergeLockSecret(_ss.Load().RoomLockSecrets, _vm.RoomId.Trim(), _vm.LockSecret),
    };

    public void SaveSettingsNow() => _ss.Save(SaveSettings());

    static Dictionary<string, string>? MergeLockSecret(
        Dictionary<string, string>? existing, string roomId, string secret)
    {
        var dict = existing is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(existing, StringComparer.Ordinal);
        dict[roomId] = secret;
        return dict;
    }
}
