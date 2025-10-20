namespace cascaler.Infrastructure;

/// <summary>
/// Application-wide constants for file extensions, defaults, and configuration values.
/// </summary>
public static class Constants
{
    // Processing defaults
    public const int DefaultImageThreads = 16;
    public const int DefaultVideoThreads = 8;
    public const int DefaultScalePercent = 50;
    public const int ProcessingTimeoutSeconds = 30;
    public const int MinimumItemsForETA = 3;
    public const int InitialEstimatedDurationMinutes = 5;

    // Output naming
    public const string OutputSuffix = "-cas";

    // Supported file extensions
    public static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".ico"
    };

    public static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mov", ".mkv", ".webm", ".wmv", ".flv", ".m4v"
    };

    // Progress bar configuration
    public const char ProgressCharacter = '─';
    public const bool ProgressBarOnBottom = true;
    public const bool ShowEstimatedDuration = true;
    public const bool DisableBottomPercentage = false;
}
