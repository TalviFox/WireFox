namespace WireFox.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        public StatusViewModel StatusVM { get; }
        public TrustedNetworksViewModel TrustedNetworksVM { get; }
        public SettingsViewModel SettingsVM { get; }
        public DiagnosticsViewModel DiagnosticsVM { get; }

        public MainViewModel()
        {
            DiagnosticsVM = new DiagnosticsViewModel();
            StatusVM = new StatusViewModel();
            TrustedNetworksVM = new TrustedNetworksViewModel();
            SettingsVM = new SettingsViewModel();
        }
    }
}
