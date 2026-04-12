using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TunnelFlow.UI.Services;

namespace TunnelFlow.UI.ViewModels;

public partial class LogViewModel : ObservableObject
{
    private const int MaxLines = 500;
    private readonly IUiLogSink _logSink;

    public ObservableCollection<LogLineViewModel> Lines { get; } = [];

    public IRelayCommand ClearCommand { get; }

    public LogViewModel(IUiLogSink? logSink = null)
    {
        _logSink = logSink ?? new UiFileLogSink();

        ClearCommand = new RelayCommand(() =>
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                Lines.Clear();
                return;
            }

            dispatcher.InvokeAsync(() => Lines.Clear());
        });
    }

    public void AddLine(string source, string level, string message)
    {
        var line = new LogLineViewModel(source, level, message);
        try
        {
            _logSink.WriteLine(line.Display);
        }
        catch
        {
            // Keep UI log rendering alive even if the file sink fails.
        }

        void AppendLine()
        {
            if (Lines.Count >= MaxLines)
                Lines.RemoveAt(0);
            Lines.Add(line);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            AppendLine();
            return;
        }

        dispatcher.InvokeAsync(AppendLine);
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
