using System.Windows;
using TunnelFlow.UI.Services;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.UI;

public partial class App : Application
{
    private ServiceClient? _serviceClient;
    private MainViewModel? _mainViewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _serviceClient = new ServiceClient();
        _mainViewModel = new MainViewModel(_serviceClient);

        var window = new MainWindow { DataContext = _mainViewModel };
        window.Show();

        await _mainViewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceClient?.Dispose();
        base.OnExit(e);
    }
}
