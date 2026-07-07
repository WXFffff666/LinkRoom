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

        // Wire up shared node checkbox to enable/disable URL textbox
        IsSharedNodeEnabled.Checked += (_, _) => SharedNodeUrls.IsEnabled = true;
        IsSharedNodeEnabled.Unchecked += (_, _) => SharedNodeUrls.IsEnabled = false;

        // Log viewer toggle via status click
        StatusText.MouseDown += (_, _) =>
        {
            LogViewer.Visibility = LogViewer.Visibility == Visibility.Collapsed
                ? Visibility.Visible
                : Visibility.Collapsed;
        };
    }

    /// <summary>Updates the status indicator and text.</summary>
    public void SetStatus(string status, string? detail = null, string? color = null)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = status;
            DetailText.Text = detail ?? "";
            if (color != null && StatusIndicator is Ellipse ellipse)
                ellipse.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        });
    }

    /// <summary>Appends a log line to the viewer.</summary>
    public void AppendLog(string line)
    {
        Dispatcher.Invoke(() => LogViewer.AppendText(line + Environment.NewLine));
    }
}
