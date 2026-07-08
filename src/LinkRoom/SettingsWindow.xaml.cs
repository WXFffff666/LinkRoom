using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using LinkRoom.Core;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;
using STUN.Client;
using STUN.Enums;

namespace LinkRoom;

public partial class SettingsWindow : Window
{
    public SettingsWindow(Gui.MainViewModel vm) { InitializeComponent(); DataContext = vm; }

    void Close_Click(object s, RoutedEventArgs e) => Close();
    void ScanGamePorts_Click(object s, RoutedEventArgs e) { GamePortResult.Text = "scanning..."; var open = GamePortScanner.ScanListeningGamePorts(); GamePortResult.Text = open.Count == 0 ? "none" : string.Join(", ", open.Select(p => $"{p.Name}({p.Port})")); }

    async void TestNat_Click(object s, RoutedEventArgs e)
    {
        NatTestResult.Text = "检测中...";
        var sb = new System.Text.StringBuilder();
        var servers = new (string, int)[] { ("stun.l.google.com",19302),("stun1.l.google.com",19302),("stun.hot-chilli.net",3478),("stun.fitauto.ru",3478),("stun.syncthing.net",3478),("stun.internetcalls.com",3478),("stun.voip.aebc.com",3478),("stun.voipbuster.com",3478),("stun.voipstunt.com",3478) };
        foreach (var (host, port) in servers)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var addrs = await Dns.GetHostAddressesAsync(host, cts.Token);
                var ip = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ip == null) { sb.AppendLine($"⏭ {host} — no IPv4"); continue; }
                using var client = new StunClient5389UDP(new IPEndPoint(ip, port), new IPEndPoint(IPAddress.Any, 0)) { ReceiveTimeout = TimeSpan.FromSeconds(10) };
                await client.QueryAsync(cts.Token);
                var nat = Classify(client.State);
                sb.AppendLine($"✅ {host} → {nat} ({client.State.PublicEndPoint})");
            }
            catch (OperationCanceledException) { sb.AppendLine($"⏱ {host} — timeout"); }
            catch { sb.AppendLine($"❌ {host} — error"); }
        }
        NatTestResult.Text = sb.Length == 0 ? "所有服务器无响应" : sb.ToString();
    }

    static Network.NatType Classify(STUN.StunResult.StunResult5389 s) => s.MappingBehavior switch
    {
        MappingBehavior.EndpointIndependent => s.FilteringBehavior switch { FilteringBehavior.EndpointIndependent => Network.NatType.FullCone, FilteringBehavior.AddressDependent => Network.NatType.RestrictedCone, FilteringBehavior.AddressAndPortDependent => Network.NatType.PortRestrictedCone, _ => Network.NatType.Unknown },
        MappingBehavior.AddressDependent => Network.NatType.Symmetric,
        MappingBehavior.AddressAndPortDependent => Network.NatType.Symmetric,
        _ => Network.NatType.Unknown
    };

    void RunSelfCheck_Click(object s, RoutedEventArgs e) { SelfCheckResult.Text = "检查中..."; var r = DoSelfCheck(); SelfCheckResult.Text = r; }

    string DoSelfCheck()
    {
        var sb = new System.Text.StringBuilder(); var pass = 0; var fail = 0;
        var rd = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkRoom", "runtime", "2.6.4");
        foreach (var f in new[] { "easytier-core.exe", "easytier-cli.exe", "wintun.dll", "Packet.dll", "WinDivert64.sys" })
        { if (System.IO.File.Exists(System.IO.Path.Combine(rd, f))) { sb.AppendLine($"✅ {f}"); pass++; } else { sb.AppendLine($"❌ {f}"); fail++; } }
        try { using var p = new System.Net.NetworkInformation.Ping(); if (p.Send("8.8.8.8", 2000).Status == System.Net.NetworkInformation.IPStatus.Success) { sb.AppendLine("✅ 网络"); pass++; } else { sb.AppendLine("❌ 网络"); fail++; } } catch { sb.AppendLine("❌ 网络"); fail++; }
        try { var w = System.Security.Principal.WindowsIdentity.GetCurrent(); if (new System.Security.Principal.WindowsPrincipal(w).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) { sb.AppendLine("✅ 管理员"); pass++; } else { sb.AppendLine("⚠️ 非管理员"); } } catch { }
        sb.AppendLine($"\n{pass}通过/{fail}失败");
        return sb.ToString();
    }
}