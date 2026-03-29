using System.ComponentModel;
using System.Windows;

namespace TunnelFlow.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        Application.Current.Shutdown();
    }
}
