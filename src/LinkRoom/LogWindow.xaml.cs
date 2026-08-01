using System.Windows;

namespace LinkRoom;

public partial class LogWindow : Window
{
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _source;

    public LogWindow(System.Collections.ObjectModel.ObservableCollection<string> source)
    {
        InitializeComponent();
        _source = source;
        RefreshLog();
        _source.CollectionChanged += OnSourceChanged;
        // Avoid leaking the subscription into the static LogLines collection (BUG-14).
        Closed += (_, _) => _source.CollectionChanged -= OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => Dispatcher.Invoke(RefreshLog);

    private void RefreshLog()
    {
        LogContent.Text = string.Join(Environment.NewLine, _source);
        LogContent.ScrollToEnd();
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _source.Clear();
    private void Copy_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(LogContent.Text);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}