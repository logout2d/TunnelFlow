using Microsoft.Extensions.Logging;

namespace TunnelFlow.Service.Logging;

internal sealed class ServiceFileLoggerProvider : ILoggerProvider
{
    private readonly string _logPath;
    private readonly object _writeLock = new();

    public ServiceFileLoggerProvider(string logPath)
    {
        _logPath = logPath;

        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public ILogger CreateLogger(string categoryName) => new ServiceFileLogger(categoryName, WriteLine);

    public void Dispose()
    {
    }

    private void WriteLine(string line)
    {
        lock (_writeLock)
        {
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
    }

    private sealed class ServiceFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly Action<string> _writeLine;

        public ServiceFileLogger(string categoryName, Action<string> writeLine)
        {
            _categoryName = categoryName;
            _writeLine = writeLine;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
            var message = formatter(state, exception);
            var line = $"{timestamp} [{logLevel}] {_categoryName}: {message}";

            if (exception is not null)
            {
                line = $"{line}{Environment.NewLine}{exception}";
            }

            _writeLine(line);
        }
    }
}
