using Microsoft.Extensions.Logging;

namespace nathanbutlerDEV.cascaler.Infrastructure;

/// <summary>
/// Logger provider that creates progress-bar-aware console loggers.
/// These loggers automatically route console output through ShellProgressBar
/// when a progress bar is active, preventing visual conflicts.
/// </summary>
public class ProgressBarAwareConsoleLoggerProvider : ILoggerProvider
{
    private readonly IProgressBarContext _progressBarContext;
    private readonly LogLevel _minLevel;

    public ProgressBarAwareConsoleLoggerProvider(
        IProgressBarContext progressBarContext,
        LogLevel minLevel = LogLevel.Information)
    {
        _progressBarContext = progressBarContext;
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ProgressBarAwareConsoleLogger(categoryName, _progressBarContext, _minLevel);
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
