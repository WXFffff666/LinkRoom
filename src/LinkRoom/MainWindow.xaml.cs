using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LinkRoom.Core;

namespace LinkRoom;

public partial class MainWindow : Window, IMainWindowView
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) { ShowInTaskbar = false; TrayHelper.Show(this); }
            else if (WindowState == WindowState.Normal) { ShowInTaskbar = true; TrayHelper.Hide(); }
        };
        Closing += (_, _) => TrayHelper.Hide();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x0203) // WM_LBUTTONDBLCLK on tray icon
        {
            Show(); WindowState = WindowState.Normal; Activate();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm)
            vm.Password = ((PasswordBox)sender).Password;
    }

    private void CreatedRoomId_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CreatedRoomId.Text.Length > 0)
        {
            Clipboard.SetText(CreatedRoomId.Text);
            MessageBox.Show("房间号已复制到剪贴板！", "LinkRoom");
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm) { var d = new SettingsWindow(vm); d.Owner = this; d.ShowDialog(); }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm) { var d = new LogWindow(Gui.MainViewModel.LogLines); d.Owner = this; d.ShowDialog(); }
    }

    public void ShowCreatedRoom(string id) => Dispatcher.Invoke(() => { CreatedRoomId.Text = id; CreatedRoomPanel.Visibility = Visibility.Visible; });
    public string GetCreatePassword() => PasswordBox.Password;
    public void SetPasswordText(string pw) => Dispatcher.Invoke(() => PasswordBox.Password = pw);
    public void AppendLog(string line) => Dispatcher.Invoke(() => Gui.MainViewModel.LogLines.Add(line));

#pragma warning disable VSTHRD100
    private async void UpdateLabel_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
#pragma warning restore VSTHRD100
    {
        UpdateLabel.Text = "检查中...";
        try { using var hc = new System.Net.Http.HttpClient(); hc.DefaultRequestHeaders.UserAgent.ParseAdd("LinkRoom");
            var json = await hc.GetStringAsync("https://api.github.com/repos/WXFffff666/LinkRoom/releases/latest");
            var tag = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("tag_name").GetString();
            UpdateLabel.Text = tag == "v1.2.0" ? "已是最新" : $"🆕 {tag} 可用";
        } catch { UpdateLabel.Text = "检查失败"; }
    }
}