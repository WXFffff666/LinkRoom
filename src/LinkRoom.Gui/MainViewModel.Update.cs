using CommunityToolkit.Mvvm.ComponentModel;
using LinkRoom.Core;

namespace LinkRoom.Gui;

public partial class MainViewModel
{
    [ObservableProperty] bool _isUpnpDisabled = true, _autoCheckUpdate = true, _firstRunCompleted;
    [ObservableProperty] bool _ipv6Only, _enableSocks5, _roomLocked;
    [ObservableProperty] int _socks5Port = 1080;
    [ObservableProperty] string? _skippedUpdateVersion;
    [ObservableProperty] string _updateStatus = "", _pathDiagram = "", _shortLinkText = "";
    [ObservableProperty] bool _isProgressVisible;
    [ObservableProperty] double _progressValue;
    [ObservableProperty] string _progressText = "";

    partial void OnIsUpnpDisabledChanged(bool value) => SaveSettingsNow();
    partial void OnAutoCheckUpdateChanged(bool value) => SaveSettingsNow();
    partial void OnIpv6OnlyChanged(bool value) => SaveSettingsNow();
    partial void OnEnableSocks5Changed(bool value) => SaveSettingsNow();
    partial void OnSocks5PortChanged(int value) => SaveSettingsNow();
    partial void OnRoomLockedChanged(bool value) => SaveSettingsNow();
    partial void OnListenerPortChanged(int value) => SaveSettingsNow();
    partial void OnMtuChanged(int value) => SaveSettingsNow();
    partial void OnUseLanModeChanged(bool value) { AppPaths.Configure(PortableMode); SaveSettingsNow(); }
    partial void OnIsSharedNodeEnabledChanged(bool value) => SaveSettingsNow();
    partial void OnSharedNodeUrlsChanged(string value) => SaveSettingsNow();
    partial void OnEnableSecureModeChanged(bool value) => SaveSettingsNow();
    partial void OnSharedNodePublicKeyChanged(string value) => SaveSettingsNow();
    partial void OnCustomStunServersChanged(string value) => SaveSettingsNow();
    partial void OnPreferIPv6Changed(bool value) => SaveSettingsNow();
    partial void OnDarkModeChanged(bool value) => SaveSettingsNow();
    partial void OnIsHostModeChanged(bool value) => SaveSettingsNow();
}
