using System.ComponentModel;
using System.Windows.Forms;
using System.Windows;
using TunnelFlow.UI.Services;
using TunnelFlow.UI.ViewModels;
using DrawingIcon = System.Drawing.Icon;
using SystemIcons = System.Drawing.SystemIcons;

namespace TunnelFlow.UI;

public partial class MainWindow : Window
{
    private readonly TrayWindowLifecycle _trayLifecycle = new();
    private bool _allowClose;
    private bool _shutdownInProgress;
    private NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        InitializeTrayIcon();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (!_allowClose && _trayLifecycle.ShouldHideOnClose())
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

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

    protected override void OnClosed(EventArgs e)
    {
        DisposeTrayIcon();
        base.OnClosed(e);
    }

    private void InitializeTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitFromTray));

        _trayIcon = new NotifyIcon
        {
            Text = "TunnelFlow",
            Icon = LoadTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
    }

    private static DrawingIcon LoadTrayIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                return DrawingIcon.ExtractAssociatedIcon(processPath) ?? SystemIcons.Application;
            }
        }
        catch
        {
            // Fall back to a standard shell icon if the executable icon cannot be read.
        }

        return SystemIcons.Application;
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Focus();
    }

    private void ExitFromTray()
    {
        _trayLifecycle.RequestExit();
        ShowInTaskbar = true;
        Show();
        Close();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Dispose();
        _trayIcon = null;
    }
}
