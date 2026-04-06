using System.ComponentModel;
using System.Windows;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.UI;

public partial class MainWindow : Window
{
    private bool _allowClose;
    private bool _shutdownInProgress;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_allowClose || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (_shutdownInProgress)
        {
            e.Cancel = true;
            return;
        }

        if (!viewModel.RequiresGracefulShutdown)
        {
            return;
        }

        e.Cancel = true;
        _shutdownInProgress = true;
        _ = CloseGracefullyAsync(viewModel);
    }

    private async Task CloseGracefullyAsync(MainViewModel viewModel)
    {
        try
        {
            await viewModel.ShutdownForApplicationExitAsync();
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _allowClose = true;
                Close();
            });
        }
    }
}
