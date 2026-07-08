using System.Windows;

namespace LinkRoom;

public partial class PeerListWindow : Window
{
    public PeerListWindow(System.Collections.ObjectModel.ObservableCollection<string> peers)
    {
        InitializeComponent();
        PeerList.ItemsSource = peers;
    }
}