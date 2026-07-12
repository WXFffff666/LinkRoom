using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LinkRoom.Core;
using iNKORE.UI.WPF.Modern;

namespace LinkRoom;

public partial class MainWindow : Window, IMainWindowView
{
    public MainWindow()
    {
        InitializeComponent();
        UpdateLabel.Text = "v" + App.Version;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is Gui.MainViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == "DarkMode")
                        ThemeManager.Current.ApplicationTheme = vm.DarkMode ? ApplicationTheme.Dark : ApplicationTheme.Light;
                    if (e.PropertyName == "UpdateStatus" && !string.IsNullOrEmpty(vm.UpdateStatus))
                        SetUpdateLabel(vm.UpdateStatus);
                };
            }
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) { ShowInTaskbar = false; TrayHelper.Show(this); }
            else { ShowInTaskbar = true; TrayHelper.Hide(); }
        };
        Closing += (_, _) => TrayHelper.Hide();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(h)?.AddHook((IntPtr hw, int m, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (m == 0x0203) { Show(); WindowState = WindowState.Normal; Activate(); handled = true; }
                return IntPtr.Zero;
            });
        };
    }

    void PasswordBox_PasswordChanged(object s, RoutedEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm) vm.Password = ((PasswordBox)s).Password;
    }

    void CreatedRoomId_DoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CreatedRoomId.Text.Length > 0) { Clipboard.SetText(CreatedRoomId.Text); MessageBox.Show("已复制房间号"); }
    }

    void LinkCode_DoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (LinkCodeText.Text.Length > 0) { Clipboard.SetText(LinkCodeText.Text); MessageBox.Show("已复制联机链接"); }
    }

    void CopyVirtualIp_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm) vm.CopyVirtualIpCommand.Execute(null);
    }

    void OpenSettings_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm) { var d = new SettingsWindow(vm) { Owner = this }; d.ShowDialog(); }
    }

    void OpenPeerList_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm) new PeerListWindow(vm) { Owner = this }.Show();
    }

    void OpenLog_Click(object s, RoutedEventArgs e)
    {
        new LogWindow(Gui.MainViewModel.LogLines) { Owner = this }.ShowDialog();
    }

    void History_Click(object s, RoutedEventArgs e)
    {
        if (s is Button b && b.Tag is string room && DataContext is Gui.MainViewModel vm)
            vm.RoomId = room;
    }

    public void ShowCreatedRoom(string id, string? linkCode = null, string? qrPayload = null) => Dispatcher.Invoke(() =>
    {
        CreatedRoomId.Text = id;
        LinkCodeText.Text = linkCode ?? "";
        CreatedRoomPanel.Visibility = Visibility.Visible;
        var payload = qrPayload ?? linkCode ?? id;
        QrCodeImage.Source = QrCodeHelper.Generate(payload);
        QrCodePanel.Visibility = QrCodeImage.Source != null ? Visibility.Visible : Visibility.Collapsed;
    });

    public string GetCreatePassword() => PasswordBox.Password;
    public void SetPasswordText(string pw) => Dispatcher.Invoke(() => PasswordBox.Password = pw);
    public void AppendLog(string line) => Dispatcher.Invoke(() => Gui.MainViewModel.LogLines.Add(line));
    public void SetUpdateLabel(string text) => Dispatcher.Invoke(() => UpdateLabel.Text = text);

    async void UpdateCheck_Click(object s, RoutedEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm)
            await vm.CheckUpdateManualCommand.ExecuteAsync(null);
    }

    async void UpdateLabel_MouseDown(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm)
            await vm.CheckUpdateManualCommand.ExecuteAsync(null);
    }
}
