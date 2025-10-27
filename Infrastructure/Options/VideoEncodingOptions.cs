using System.ComponentModel.DataAnnotations;

namespace nathanbutlerDEV.cascaler.Infrastructure.Options;

/// <summary>
/// Configuration options for video encoding.
/// </summary>
public class VideoEncodingOptions
{
    /// <summary>
    /// Constant Rate Factor for video quality (0-51, lower is better quality).
    /// </summary>
    [Range(0, 51)]
    public int DefaultCRF { get; set; } = 23;

    /// <summary>
    /// Encoding preset (ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow).
    /// </summary>
    [RegularExpression("^(ultrafast|superfast|veryfast|faster|fast|medium|slow|slower|veryslow)$")]
    public string DefaultPreset { get; set; } = "medium";

    /// <summary>
    /// Pixel format for video output (most compatible is yuv420p).
    /// </summary>
    public string DefaultPixelFormat { get; set; } = "yuv420p";

    /// <summary>
    /// Video codec to use (libx264 for H.264 encoding).
    /// </summary>
    public string DefaultCodec { get; set; } = "libx264";
}
