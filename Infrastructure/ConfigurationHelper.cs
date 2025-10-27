using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace nathanbutlerDEV.cascaler.Infrastructure;

/// <summary>
/// Utility class for building application configuration from multiple sources.
/// </summary>
public static class ConfigurationHelper
{
    /// <summary>
    /// Gets the user configuration directory path based on the operating system.
    /// </summary>
    /// <returns>Path to user config directory (e.g., ~/.config/cascaler or %APPDATA%\cascaler)</returns>
    public static string GetUserConfigDirectory()
    {
        var baseDir = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(baseDir, "cascaler");
    }

    /// <summary>
    /// Gets the full path to the user configuration file.
    /// </summary>
    /// <returns>Path to appsettings.json in user config directory</returns>
    public static string GetUserConfigPath()
    {
        return Path.Combine(GetUserConfigDirectory(), "appsettings.json");
    }

    /// <summary>
    /// Gets the user log directory path based on the operating system.
    /// </summary>
    /// <returns>Path to user log directory (e.g., ~/.config/cascaler/logs or %APPDATA%\cascaler\logs)</returns>
    public static string GetUserLogDirectory()
    {
        return Path.Combine(GetUserConfigDirectory(), "logs");
    }

    /// <summary>
    /// Ensures the log directory exists, creating it if necessary.
    /// </summary>
    public static void EnsureLogDirectoryExists()
    {
        var logDir = GetUserLogDirectory();
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }
    }

    /// <summary>
    /// Cleans up old log files older than the specified number of days.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain logs (default: 7)</param>
    public static void CleanupOldLogs(int retentionDays = 7)
    {
        var logDir = GetUserLogDirectory();
        if (!Directory.Exists(logDir))
            return;

        var cutoffDate = DateTime.Now.AddDays(-retentionDays);
        var logFiles = Directory.GetFiles(logDir, "cascaler-*.log");

        foreach (var logFile in logFiles)
        {
            var fileInfo = new FileInfo(logFile);
            if (fileInfo.LastWriteTime < cutoffDate)
            {
                try
                {
                    File.Delete(logFile);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }
    }

    /// <summary>
    /// Builds the application configuration from embedded defaults and optional user config file.
    /// </summary>
    /// <returns>IConfiguration with layered configuration sources</returns>
    public static IConfiguration BuildConfiguration()
    {
        var builder = new ConfigurationBuilder();

        // 1. Load embedded appsettings.json (defaults)
        var embeddedConfig = LoadEmbeddedConfiguration();
        if (embeddedConfig != null)
        {
            builder.AddJsonStream(embeddedConfig);
        }

        // 2. Load user config file if it exists
        var userConfigPath = GetUserConfigPath();
        if (File.Exists(userConfigPath))
        {
            builder.AddJsonFile(userConfigPath, optional: true, reloadOnChange: false);
        }

        return builder.Build();
    }

    /// <summary>
    /// Loads the embedded appsettings.json from the assembly resources.
    /// </summary>
    /// <returns>Stream containing the embedded configuration, or null if not found</returns>
    private static Stream? LoadEmbeddedConfiguration()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "appsettings.json";

        // Try to find the embedded resource
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            return stream;
        }

        // Fallback: try to load from file system (useful during development)
        var currentDir = AppContext.BaseDirectory;
        var configPath = Path.Combine(currentDir, "appsettings.json");

        if (File.Exists(configPath))
        {
            return new FileStream(configPath, FileMode.Open, FileAccess.Read);
        }

        return null;
    }
}
