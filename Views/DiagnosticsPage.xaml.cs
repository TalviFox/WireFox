using System.Windows.Controls;
using WireFox.ViewModels;

namespace WireFox.Views
{
    public partial class DiagnosticsPage : Page
    {
        public DiagnosticsPage(DiagnosticsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
