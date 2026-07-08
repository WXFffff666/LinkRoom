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
        NatTestResult.Text = "并发检测中（返回首个结果）...";
        var sb = new System.Text.StringBuilder();
        var servers = new[] { ("stun.l.google.com",19302),("stun1.l.google.com",19302),("stun.hot-chilli.net",3478),("stun.fitauto.ru",3478),("stun.syncthing.net",3478),("stun.internetcalls.com",3478),("stun.voip.aebc.com",3478) };
        // Concurrent: use Task.WhenAny to get first result, then cancel rest
        using var masterCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var tasks = servers.Select(svr => ProbeAsync(svr.Item1, svr.Item2, masterCts.Token)).ToList();
        try
        {
            while (tasks.Count > 0)
            {
                var done = await Task.WhenAny(tasks);
                tasks.Remove(done);
                var res = await done;
                if (res.Item2 != null) { sb.AppendLine($"✅ {res.Item1} → {res.Item2}"); break; }
                else sb.AppendLine($"❌ {res.Item1}");
            }
            masterCts.Cancel(); // cancel remaining probes
        }
        catch (OperationCanceledException) { sb.AppendLine("⏱ 所有服务器超时"); }
        NatTestResult.Text = sb.Length == 0 ? "无可用 STUN 服务器" : sb.ToString();
    }

    static async Task<(string, string?)> ProbeAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, ct);
            var ip = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip == null) return (host, null);
            using var client = new StunClient5389UDP(new IPEndPoint(ip, port), new IPEndPoint(IPAddress.Any, 0)) { ReceiveTimeout = TimeSpan.FromSeconds(8) };
            await client.QueryAsync(ct);
            var nat = Classify(client.State);
            return (host, $"{nat} ({client.State.PublicEndPoint})");
        }
        catch { return (host, null); }
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