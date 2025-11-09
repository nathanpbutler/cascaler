using System.ComponentModel.DataAnnotations;

namespace nathanbutlerDEV.cascaler.Infrastructure.Options;

/// <summary>
/// Configuration settings for image and video processing.
/// </summary>
public class ProcessingSettings
{
    /// <summary>
    /// Maximum number of threads for parallel image processing.
    /// </summary>
    [Range(1, 128)]
    public int MaxImageThreads { get; set; } = 16;

    /// <summary>
    /// Maximum number of threads for parallel video frame processing.
    /// </summary>
    [Range(1, 32)]
    public int MaxVideoThreads { get; set; } = 8;

    /// <summary>
    /// Timeout in seconds for processing a single image.
    /// </summary>
    [Range(1, 600)]
    public int ProcessingTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum number of items processed before showing ETA in progress bar.
    /// </summary>
    [Range(1, 100)]
    public int MinimumItemsForETA { get; set; } = 3;

    /// <summary>
    /// Default scale percentage when not specified.
    /// </summary>
    [Range(1, 200)]
    public int DefaultScalePercent { get; set; } = 50;

    /// <summary>
    /// Default frames per second for image-to-sequence conversion.
    /// </summary>
    [Range(1, 120)]
    public int DefaultFps { get; set; } = 25;

    /// <summary>
    /// Default output format for video frames (png, jpg, bmp, tiff).
    /// </summary>
    [RegularExpression("^(png|jpg|jpeg|bmp|tiff|tif)$")]
    public string DefaultVideoFrameFormat { get; set; } = "png";

    /// <summary>
    /// Default output format for images (png, jpg, bmp, tiff). Empty string preserves input format.
    /// </summary>
    [RegularExpression("^(png|jpg|jpeg|bmp|tiff|tif)?$")]
    public string DefaultImageOutputFormat { get; set; } = "";

    /// <summary>
    /// Default deltaX value for seam carving (0-1, controls seam curvature).
    /// </summary>
    [Range(0.0, 1.0)]
    public double DefaultDeltaX { get; set; } = 1.0;

    /// <summary>
    /// Default rigidity value for seam carving (bias for non-straight seams).
    /// </summary>
    [Range(0.0, 10.0)]
    public double DefaultRigidity { get; set; } = 1.0;

    /// <summary>
    /// Default scale-back behavior (scale processed frames back to original dimensions).
    /// </summary>
    public bool DefaultScaleBack { get; set; } = false;

    /// <summary>
    /// Default vibrato audio effect behavior for video processing.
    /// </summary>
    public bool DefaultVibrato { get; set; } = false;

    /// <summary>
    /// Preserve color metadata (color primaries, transfer characteristics, color space) from source.
    /// Essential for HDR/wide gamut content to prevent washed out colors.
    /// </summary>
    public bool PreserveColorMetadata { get; set; } = true;
}
