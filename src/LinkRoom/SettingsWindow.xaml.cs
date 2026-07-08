using System.Diagnostics;
using System.Windows;
using LinkRoom.Core;
using LinkRoom.Network;
using Microsoft.Extensions.Logging;

namespace LinkRoom;

public partial class SettingsWindow : Window
{
    public SettingsWindow(Gui.MainViewModel vm) { InitializeComponent(); DataContext = vm; }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

#pragma warning disable VSTHRD100 // async void is required for WPF event handlers
    private async void TestNat_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        NatTestResult.Text = "检测中...";
        try
        {
            var lf = LoggerFactory.Create(b => b.AddDebug());
            var detector = new StunNatDetector(lf.CreateLogger<StunNatDetector>());
            var ns = new NetworkInfoService(detector, lf.CreateLogger<NetworkInfoService>());
            var snap = await ns.CaptureAsync().ConfigureAwait(true);
            NatTestResult.Text = snap.StunReachable
                ? $"NAT: {snap.NatType}\nPublic IPv4: {snap.PublicIPv4 ?? "none"}\nUDP: {(snap.UdpReachable ? "reachable" : "blocked")}"
                : "STUN unreachable";
        }
        catch (Exception ex) { NatTestResult.Text = $"Error: {ex.Message}"; }
    }

    private void ScanGamePorts_Click(object sender, RoutedEventArgs e)
    {
        GamePortResult.Text = "扫描中...";
        var open = GamePortScanner.ScanListeningGamePorts();
        GamePortResult.Text = open.Count == 0
            ? "未检测到已知游戏端口。可手动在'监听端口'中设置。"
            : "检测到: " + string.Join(", ", open.Select(p => $"{p.Name}({p.Port})"));
    }

    private void RunSelfCheck_Click(object sender, RoutedEventArgs e)
    {
        SelfCheckResult.Text = "正在运行自检...";
        var result = RunSelfCheck();
        SelfCheckResult.Text = result;
    }

    private string RunSelfCheck()
    {
        var results = new System.Text.StringBuilder();
        int pass = 0, fail = 0;

        // 1. Check EasyTier core
        var runtimeDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkRoom", "runtime", "2.6.4");
        var core = System.IO.Path.Combine(runtimeDir, "easytier-core.exe");
        var cli = System.IO.Path.Combine(runtimeDir, "easytier-cli.exe");
        var wintun = System.IO.Path.Combine(runtimeDir, "wintun.dll");

        if (System.IO.File.Exists(core)) { results.AppendLine("✅ EasyTier 核心: 已安装"); pass++; }
        else { results.AppendLine("❌ EasyTier 核心: 未找到 — 请以管理员身份重新运行"); fail++; }

        if (System.IO.File.Exists(cli)) { results.AppendLine("✅ EasyTier CLI: 已安装"); pass++; }
        else { results.AppendLine("❌ EasyTier CLI: 未找到"); fail++; }

        if (System.IO.File.Exists(wintun)) { results.AppendLine("✅ Wintun 驱动: 已安装"); pass++; }
        else { results.AppendLine("⚠️ Wintun 驱动: 未找到 — 首次运行时需管理员权限安装"); pass++; }

        // 2. Check version
        try
        {
            var psi = new ProcessStartInfo { FileName = core, Arguments = "--version", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            var p = Process.Start(psi);
            var ver = p?.StandardOutput.ReadToEnd().Trim() ?? "unknown";
            p?.WaitForExit(2000);
            results.AppendLine($"✅ 版本: {ver}"); pass++;
        }
        catch { results.AppendLine("⚠️ 无法检测版本"); }

        // 3. Network check
        try
        {
            using var ping = new System.Net.NetworkInformation.Ping();
            var reply = ping.Send("8.8.8.8", 3000);
            if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
            { results.AppendLine("✅ 网络连接: 正常"); pass++; }
            else { results.AppendLine("❌ 网络连接: 无法访问互联网"); fail++; }
        }
        catch { results.AppendLine("❌ 网络连接: 检测失败"); fail++; }

        // 4. STUN reachability
        try
        {
            using var ping2 = new System.Net.NetworkInformation.Ping();
            var reply2 = ping2.Send("stun.l.google.com", 3000);
            if (reply2.Status == System.Net.NetworkInformation.IPStatus.Success)
            { results.AppendLine("✅ STUN 服务器: 可达"); pass++; }
            else { results.AppendLine("⚠️ STUN 服务器: 不可达 — NAT 检测可能失败"); pass++; }
        }
        catch { results.AppendLine("⚠️ STUN 服务器: 检测跳过"); }

        // 5. Admin check
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            if (principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            { results.AppendLine("✅ 管理员权限: 已获取"); pass++; }
            else { results.AppendLine("⚠️ 管理员权限: 未获取 — Wintun 驱动可能无法安装"); }
        }
        catch { results.AppendLine("⚠️ 权限检测: 跳过"); }

        results.AppendLine($"\n结果: {pass} 通过 / {fail} 失败");
        return results.ToString();
    }
}