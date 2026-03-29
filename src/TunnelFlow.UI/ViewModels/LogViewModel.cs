using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TunnelFlow.UI.ViewModels;

public partial class LogViewModel : ObservableObject
{
    private const int MaxLines = 500;

    public ObservableCollection<LogLineViewModel> Lines { get; } = [];

    public IRelayCommand ClearCommand { get; }

    public LogViewModel()
    {
        ClearCommand = new RelayCommand(() =>
            Application.Current.Dispatcher.InvokeAsync(() => Lines.Clear()));
    }

    public void AddLine(string source, string level, string message)
    {
        var line = new LogLineViewModel(source, level, message);
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (Lines.Count >= MaxLines)
                Lines.RemoveAt(0);
            Lines.Add(line);
        });
    }
}

public sealed class LogLineViewModel
{
    public string Timestamp { get; } = DateTime.Now.ToString("HH:mm:ss");
    public string Source { get; }
    public string Level { get; }
    public string Message { get; }
    public string Display => $"[{Timestamp}] [{Source}] {Level}: {Message}";

    public LogLineViewModel(string source, string level, string message)
    {
        Source = source;
        Level = level;
        Message = message;
    }
}
