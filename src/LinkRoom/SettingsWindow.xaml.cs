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
        NatTestResult.Text = "检测中...";
        var sb = new System.Text.StringBuilder();
        await vm.RunNatTestAsync(line => sb.Append(line));
        NatTestResult.Text = sb.ToString();
    }

    void RunSelfCheck_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        SelfCheckResult.Text = "检查中...";
        SelfCheckResult.Text = vm.RunSelfCheck();
    }

    async void ExportDiag_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.ExportDiagnosticsCommand.ExecuteAsync(null);
    }

    void OpenWebPanel_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.OpenWebPanelCommand.Execute(null);
    }

    async void RefreshStun_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.RefreshStunListCommand.ExecuteAsync(null);
    }
}
