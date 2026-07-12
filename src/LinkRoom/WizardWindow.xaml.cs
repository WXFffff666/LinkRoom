using System.Windows;
using LinkRoom.Gui;

namespace LinkRoom;

public partial class WizardWindow : Window
{
    readonly MainViewModel _vm;
    int _step = 1;

    public WizardWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        LanModeCheck.IsChecked = vm.UseLanMode;
        AutoUpdateCheck.IsChecked = vm.AutoCheckUpdate;
        PortableCheck.IsChecked = vm.PortableMode;
    }

    void Next_Click(object s, RoutedEventArgs e)
    {
        if (_step == 1)
        {
            _step = 2;
            StepTitle.Text = "步骤 2/3 — 网络检测";
            StepDesc.Text = "即将检测 NAT 类型，请确保网络正常。";
            WizardProgress.Value = 2;
            return;
        }
        if (_step == 2)
        {
            _step = 3;
            StepTitle.Text = "步骤 3/3 — 完成";
            StepDesc.Text = "设置已保存。点击完成开始使用 LinkRoom！";
            WizardProgress.Value = 3;
            ((System.Windows.Controls.Button)s).Content = "完成";
            return;
        }
        ApplyAndClose();
    }

    void Skip_Click(object s, RoutedEventArgs e) => ApplyAndClose();

    void ApplyAndClose()
    {
        _vm.UseLanMode = LanModeCheck.IsChecked == true;
        _vm.AutoCheckUpdate = AutoUpdateCheck.IsChecked == true;
        _vm.PortableMode = PortableCheck.IsChecked == true;
        _vm.FirstRunCompleted = true;
        _vm.SaveSettingsNow();
        Close();
    }
}
