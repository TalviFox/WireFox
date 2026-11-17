using WireFox.ViewModels;
using Wpf.Ui.Controls;

namespace WireFox.Views
{
    public partial class AboutWindow : FluentWindow
    {
        public AboutWindow(SettingsViewModel settingsViewModel)
        {
            InitializeComponent();
            DataContext = settingsViewModel;
        }
    }
}
