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

    /// <summary>Forwards PasswordBox changes to the ViewModel's Password property.</summary>
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is Gui.MainViewModel vm)
        {
            vm.Password = ((PasswordBox)sender).Password;
        }
    }

    /// <summary>Updates the status indicator color and detail text from code.</summary>
    public void SetStatus(string status, string? detail = null, string? color = null)
    {
        Dispatcher.Invoke(() =>
        {
            if (color != null && StatusIndicator is Ellipse ellipse)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                ellipse.Fill = brush;
            }
            DetailText.Text = detail ?? "";
        });
    }

    /// <summary>Appends a log line to the viewer.</summary>
    public void AppendLog(string line)
    {
        Dispatcher.Invoke(() => LogViewer.AppendText(line + Environment.NewLine));
    }
}