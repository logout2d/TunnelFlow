using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.UI.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
    }

    private void ProjectLink_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not AboutViewModel viewModel || string.IsNullOrWhiteSpace(viewModel.ProjectUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = viewModel.ProjectUrl,
            UseShellExecute = true
        });

        e.Handled = true;
    }
}
