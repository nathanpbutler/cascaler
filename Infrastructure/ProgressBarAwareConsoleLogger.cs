using Microsoft.Extensions.Logging;

namespace nathanbutlerDEV.cascaler.Infrastructure;

/// <summary>
/// Custom console logger that routes output through ShellProgressBar when active
/// to prevent visual conflicts between logging and progress bar updates.
/// </summary>
public class ProgressBarAwareConsoleLogger : ILogger
{
    private readonly string _categoryName;
    private readonly IProgressBarContext _progressBarContext;
    private readonly LogLevel _minLevel;

    public ProgressBarAwareConsoleLogger(
        string categoryName,
        IProgressBarContext progressBarContext,
        LogLevel minLevel)
    {
        _categoryName = categoryName;
        _progressBarContext = progressBarContext;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= _minLevel;
    }

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

        var message = formatter(state, exception);

        // Format message based on log level (matching CleanConsoleFormatter behavior)
        string formattedMessage;
        if (logLevel >= LogLevel.Warning)
        {
            // For warnings/errors, show full context
            var levelString = GetLogLevelString(logLevel);
            formattedMessage = $"[{levelString}] {_categoryName}: {message}";

            if (exception != null)
            {
                formattedMessage += Environment.NewLine + exception.ToString();
            }
        }
        else
        {
            // For Information and below, just show the clean message
            formattedMessage = message;
        }

        // Route output based on whether progress bar is active
        var progressBar = _progressBarContext.Current;
        if (progressBar != null)
        {
            // Progress bar is active - use its WriteLine to avoid conflicts
            progressBar.WriteLine(formattedMessage);
        }
        else
        {
            // No progress bar - write directly to console
            Console.WriteLine(formattedMessage);
        }
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRITICAL",
            _ => logLevel.ToString()
        };
    }
}
