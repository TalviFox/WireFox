using System.Windows.Controls;
using System.Windows.Input;
using WireFox.ViewModels;

namespace WireFox.Views
{
    public partial class StatusPage : Page
    {
        public StatusPage(StatusViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            PreviewMouseWheel += OnPreviewMouseWheel;
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled && PageScrollViewer != null)
            {
                PageScrollViewer.ScrollToVerticalOffset(PageScrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }
    }
}
