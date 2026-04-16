using System.IO;
using System.Text;
using TunnelFlow.Core;

namespace TunnelFlow.UI.Services;

internal sealed class UiFileLogSink : IUiLogSink
{
    private readonly string _logFilePath;
    private readonly object _gate = new();

    public UiFileLogSink(string? baseDirectory = null)
    {
        _logFilePath = RuntimePaths.Create(baseDirectory).UiLogPath;
    }

    public void WriteLine(string line)
    {
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            lock (_gate)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // UI logging must remain best-effort and never break the app.
        }
    }
}
