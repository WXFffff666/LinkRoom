using System.Windows;
using System.Windows.Controls;
using LinkRoom.Core;

namespace LinkRoom;

public partial class MainWindow : Window, IMainWindowView
{
    public MainWindow() => InitializeComponent();

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
    public void AppendLog(string line) => Dispatcher.Invoke(() => Gui.MainViewModel.LogLines.Add(line));
}