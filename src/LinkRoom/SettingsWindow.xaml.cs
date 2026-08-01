using System.Windows;
using LinkRoom.Core;
using LinkRoom.Gui;

namespace LinkRoom;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    void Close_Click(object s, RoutedEventArgs e) => Close();

    void ScanGamePorts_Click(object s, RoutedEventArgs e)
    {
        GamePortResult.Text = "扫描中...";
        var open = GamePortScanner.ScanListeningGamePorts();
        GamePortResult.Text = open.Count == 0 ? "未检测到游戏端口" : string.Join(", ", open.Select(p => $"{p.Name}({p.Port})"));
    }

    async void TestNat_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            NatTestResult.Text = "检测中...";
            var sb = new System.Text.StringBuilder();
            await vm.RunNatTestAsync(line => sb.Append(line));
            NatTestResult.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            vm.L($"NAT 检测失败: {ex}");
            NatTestResult.Text = $"检测失败: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    void RunSelfCheck_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        SelfCheckResult.Text = "检查中...";
        SelfCheckResult.Text = vm.RunSelfCheck();
    }

    async void ExportDiag_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            await vm.ExportDiagnosticsCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            vm.L($"导出诊断失败: {ex}");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    void OpenWebPanel_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.OpenWebPanelCommand.Execute(null);
    }

    async void RefreshStun_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            await vm.RefreshStunListCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            vm.L($"更新 STUN 列表失败: {ex}");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    void RefreshNetwork_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.RefreshNetworkCommand.Execute(null);
    }

    async void CheckUpdate_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            await vm.CheckUpdateManualCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            vm.L($"检查更新失败: {ex}");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    async void ExportConfig_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            await vm.ExportConfigCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            vm.L($"导出配置失败: {ex}");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    async void ImportConfig_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            await vm.ImportConfigCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            vm.L($"导入配置失败: {ex}");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    void ScanMods_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.ScanModsCommand.Execute(null);
    }

    async void CheckEtVersion_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            await vm.CheckEasyTierVersionCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            vm.L($"检查 EasyTier 版本失败: {ex}");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
