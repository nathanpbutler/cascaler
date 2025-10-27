using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace nathanbutlerDEV.cascaler.Infrastructure;

/// <summary>
/// Custom console formatter that outputs clean messages for Information level
/// and detailed output for Warning/Error levels.
/// </summary>
public sealed class CleanConsoleFormatter : ConsoleFormatter
{
    public CleanConsoleFormatter() : base("clean")
    {
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);

        if (logEntry.LogLevel >= LogLevel.Warning)
        {
            // For warnings/errors, show full context
            var levelString = GetLogLevelString(logEntry.LogLevel);
            textWriter.WriteLine($"[{levelString}] {logEntry.Category}: {message}");

            if (logEntry.Exception != null)
            {
                textWriter.WriteLine(logEntry.Exception.ToString());
            }
        }
        else
        {
            // For Information and below, just show the clean message
            textWriter.WriteLine(message);
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
