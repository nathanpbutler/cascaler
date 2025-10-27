using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace nathanbutlerDEV.cascaler.Infrastructure;

/// <summary>
/// Simple file logging provider for Microsoft.Extensions.Logging.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly StreamWriter _writer;

    public FileLoggerProvider(string logFilePath)
    {
        _logFilePath = logFilePath;

        // Ensure directory exists
        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Open file for append
        _writer = new StreamWriter(logFilePath, append: true)
        {
            AutoFlush = true
        };
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _writer));
    }

    public void Dispose()
    {
        _loggers.Clear();
        _writer?.Dispose();
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly StreamWriter _writer;

        public FileLogger(string categoryName, StreamWriter writer)
        {
            _categoryName = categoryName;
            _writer = writer;
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
                return;

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var message = formatter(state, exception);
            var logEntry = $"{timestamp} [{logLevel}] {_categoryName}: {message}";

            if (exception != null)
            {
                logEntry += Environment.NewLine + exception.ToString();
            }

            lock (_writer)
            {
                _writer.WriteLine(logEntry);
            }
        }
    }
}
