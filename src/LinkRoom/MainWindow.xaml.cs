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
            MessageBox.Show("房间号已复制到剪贴板！", "LinkRoom", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public void ShowCreatedRoom(string roomId)
    {
        Dispatcher.Invoke(() => { CreatedRoomId.Text = roomId; CreatedRoomPanel.Visibility = Visibility.Visible; });
    }

    public string GetCreatePassword() => CreatePasswordBox.Password;

    public void AppendLog(string line)
    {
        Dispatcher.Invoke(() => { LogText.AppendText(line + Environment.NewLine); LogText.ScrollToEnd(); });
    }
}