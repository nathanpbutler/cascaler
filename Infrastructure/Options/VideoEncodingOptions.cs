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

    /// <summary>
    /// Prefer libx265 (HEVC) codec for HDR content (better 10-bit support).
    /// </summary>
    public bool PreferHEVCForHDR { get; set; } = true;

    /// <summary>
    /// Maximum bit depth for video encoding (8, 10, or 12). HDR content typically uses 10-bit.
    /// </summary>
    [Range(8, 12)]
    public int MaxBitDepth { get; set; } = 10;

    /// <summary>
    /// Automatically detect and preserve HDR metadata from source videos.
    /// </summary>
    public bool AutoDetectHDR { get; set; } = true;
}
