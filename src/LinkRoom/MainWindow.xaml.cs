using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LinkRoom;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm)
            vm.Password = ((PasswordBox)sender).Password;
    }

    public void SetStatus(string status, string? detail = null, string? color = null)
    {
        Dispatcher.Invoke(() =>
        {
            if (color != null && StatusIndicator is Ellipse ellipse)
                ellipse.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            if (DetailPanel is TextBlock tb)
                tb.Text = detail ?? "";
        });
    }
}