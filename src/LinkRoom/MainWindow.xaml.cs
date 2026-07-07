using System.Windows;
using System.Windows.Controls;

namespace LinkRoom;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm)
            vm.Password = ((PasswordBox)sender).Password;
    }
}