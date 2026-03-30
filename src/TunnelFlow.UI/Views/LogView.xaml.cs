using System.Collections.Specialized;
using System.Windows.Controls;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.UI.Views;

public partial class LogView : UserControl
{
    private LogViewModel? _vm;

    public LogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.Lines.CollectionChanged -= OnLinesChanged;

        _vm = DataContext as LogViewModel;

        if (_vm is not null)
            _vm.Lines.CollectionChanged += OnLinesChanged;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        if (LogListBox.Items.Count == 0) return;

        // Defer scroll to after the layout pass — calling ScrollIntoView on a
        // ListBox that has not yet completed its first render measure/arrange
        // throws an InvalidOperationException (or crashes the visual tree).
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (LogListBox.Items.Count > 0)
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
            }
            catch { }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
