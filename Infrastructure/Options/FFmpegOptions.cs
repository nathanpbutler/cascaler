namespace nathanbutlerDEV.cascaler.Infrastructure.Options;

/// <summary>
/// Configuration options for FFmpeg library detection and initialization.
/// </summary>
public class FFmpegOptions
{
    /// <summary>
    /// Path to FFmpeg library directory. Leave empty for auto-detection.
    /// </summary>
    public string LibraryPath { get; set; } = string.Empty;

    /// <summary>
    /// Enable automatic detection of FFmpeg libraries if LibraryPath is not specified.
    /// </summary>
    public bool EnableAutoDetection { get; set; } = true;
}
