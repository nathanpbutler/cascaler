namespace nathanbutlerDEV.cascaler.Infrastructure;

/// <summary>
/// Application-wide constants for file extensions and immutable values.
/// For configurable values, see appsettings.json and Options classes.
/// </summary>
public static class Constants
{
    // Temporary directory naming
    public const string TempFramesFolderPrefix = "cascaler_temp_";

    // Supported file extensions
    public static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".ico"
    };

    public static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mov", ".mkv", ".webm", ".wmv", ".flv", ".m4v"
    };

    public static readonly HashSet<string> SupportedVideoOutputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv"
    };

    // Audio codec compatibility with MP4 container
    public static readonly HashSet<string> MP4CompatibleAudioCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "aac", "mp3", "ac3", "eac3", "mp2"
    };

    // Output format mappings
    public static readonly Dictionary<string, string> FormatExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        { "png", ".png" },
        { "jpg", ".jpg" },
        { "jpeg", ".jpg" },
        { "bmp", ".bmp" },
        { "tiff", ".tiff" },
        { "tif", ".tiff" }
    };

    public static readonly HashSet<string> SupportedOutputFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "bmp", "tiff", "tif"
    };
}
