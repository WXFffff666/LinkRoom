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
        DataContextChanged += (_, _) =>
        { if (DataContext is Gui.MainViewModel vm) vm.PropertyChanged += (_, e) => { if (e.PropertyName == "DarkMode") ThemeManager.Current.ApplicationTheme = vm.DarkMode ? ApplicationTheme.Dark : ApplicationTheme.Light; }; };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { ShowInTaskbar = false; TrayHelper.Show(this); } else { ShowInTaskbar = true; TrayHelper.Hide(); } };
        Closing += (_, _) => TrayHelper.Hide();
        SourceInitialized += (_, _) => { var h = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(h)?.AddHook((IntPtr hw, int m, IntPtr w, IntPtr l, ref bool handled) => { if (m == 0x0203) { Show(); WindowState = WindowState.Normal; Activate(); handled = true; } return IntPtr.Zero; }); };
    }

    void PasswordBox_PasswordChanged(object s, RoutedEventArgs e) { if (DataContext is Gui.MainViewModel vm) vm.Password = ((PasswordBox)s).Password; }
    void CreatedRoomId_DoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e) { if (CreatedRoomId.Text.Length > 0) { Clipboard.SetText(CreatedRoomId.Text); MessageBox.Show("已复制"); } }
    void OpenSettings_Click(object s, RoutedEventArgs e) { if (DataContext is Gui.MainViewModel vm) { var d = new SettingsWindow(vm); d.Owner = this; d.ShowDialog(); } }
    void OpenLog_Click(object s, RoutedEventArgs e) { if (DataContext is Gui.MainViewModel vm) { var d = new LogWindow(Gui.MainViewModel.LogLines); d.Owner = this; d.ShowDialog(); } }

    public void ShowCreatedRoom(string id) => Dispatcher.Invoke(() => { CreatedRoomId.Text = id; CreatedRoomPanel.Visibility = Visibility.Visible; });
    public string GetCreatePassword() => PasswordBox.Password;
    public void SetPasswordText(string pw) => Dispatcher.Invoke(() => PasswordBox.Password = pw);
    public void AppendLog(string line) => Dispatcher.Invoke(() => Gui.MainViewModel.LogLines.Add(line));

    async void UpdateCheck_Click(object s, RoutedEventArgs e) => await CheckUpdateAsync();
    async void UpdateLabel_MouseDown(object s, System.Windows.Input.MouseButtonEventArgs e) => await CheckUpdateAsync();

    async Task CheckUpdateAsync()
    {
        UpdateLabel.Text = "checking...";
        try { using var hc = new System.Net.Http.HttpClient(); hc.DefaultRequestHeaders.UserAgent.ParseAdd("LinkRoom"); var json = await hc.GetStringAsync("https://api.github.com/repos/WXFffff666/LinkRoom/releases/latest"); var tag = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("tag_name").GetString(); UpdateLabel.Text = tag == "v1.11.0" ? "latest" : $"new {tag}"; }
        catch { UpdateLabel.Text = "failed"; }
    }
}