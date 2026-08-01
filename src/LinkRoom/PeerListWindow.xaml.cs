using System.Windows;
using LinkRoom.Gui;

namespace LinkRoom;

public partial class PeerListWindow : Window
{
    readonly MainViewModel _vm;

    public PeerListWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        PeerList.ItemsSource = vm.Peers;
        PeerHint.Text = $"共 {vm.PeerCount} 人在线 · 格式: 角色 | IP | NAT | 延迟 | cost";
    }

    async void PingAll_Click(object s, RoutedEventArgs e)
    {
        try
        {
            await _vm.PingPeersCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _vm.L($"Ping 全部失败: {ex}");
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
