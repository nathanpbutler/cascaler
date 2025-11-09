namespace nathanbutlerDEV.cascaler.Infrastructure;

// Color space and HDR metadata enums (aligned with FFmpeg AVColor* enums)

/// <summary>
/// Color primaries defining the color gamut (chromaticity coordinates)
/// </summary>
public enum ColorPrimaries
{
    Unspecified = 2,
    BT709 = 1,       // HD/SDR (Rec.709)
    BT470M = 4,      // NTSC
    BT470BG = 5,     // PAL/SECAM
    BT601 = 6,       // SD (SMPTE 170M)
    SMPTE240M = 7,   // SMPTE 240M
    Film = 8,        // Generic film
    BT2020 = 9,      // UHD/HDR (Rec.2020)
    SMPTE428 = 10,   // DCI-P3 D65
    SMPTE431 = 11,   // DCI-P3 (Theater)
    SMPTE432 = 12,   // Display P3
    EBU3213 = 22     // EBU Tech 3213-E
}

/// <summary>
/// Transfer characteristics defining the electro-optical transfer function (EOTF)
/// </summary>
public enum TransferCharacteristic
{
    Unspecified = 2,
    BT709 = 1,       // SDR gamma (same as BT601, BT2020)
    Gamma22 = 4,     // Gamma 2.2
    Gamma28 = 5,     // Gamma 2.8
    BT601 = 6,       // SD (same as BT709)
    SMPTE240M = 7,   // SMPTE 240M
    Linear = 8,      // Linear transfer
    Log100 = 9,      // Log 100:1 range
    Log316 = 10,     // Log 316:1 range
    IEC61966_2_4 = 11, // xvYCC
    BT1361 = 12,     // BT.1361 extended color gamut
    SRGB = 13,       // sRGB/sYCC
    BT2020_10bit = 14, // BT.2020 10-bit
    BT2020_12bit = 15, // BT.2020 12-bit
    PQ = 16,         // HDR10 (SMPTE ST 2084, Perceptual Quantizer)
    SMPTE428 = 17,   // SMPTE ST 428-1
    HLG = 18         // Hybrid Log-Gamma (ARIB STD-B67)
}

/// <summary>
/// YUV color space (matrix coefficients) defining YUV ↔ RGB conversion.
/// Renamed to YuvColorSpace to avoid conflict with ImageMagick.ColorSpace.
/// </summary>
public enum YuvColorSpace
{
    RGB = 0,         // Identity (RGB, no YUV conversion)
    Unspecified = 2,
    BT709 = 1,       // HD/SDR
    FCC = 4,         // FCC
    BT470BG = 5,     // PAL/SECAM (same as BT601)
    BT601 = 6,       // SD (SMPTE 170M)
    SMPTE240M = 7,   // SMPTE 240M
    YCgCo = 8,       // YCgCo
    BT2020NC = 9,    // BT.2020 non-constant luminance
    BT2020C = 10,    // BT.2020 constant luminance
    SMPTE2085 = 11,  // SMPTE 2085
    ChromaDerivedNC = 12, // Chroma-derived non-constant luminance
    ChromaDerivedC = 13,  // Chroma-derived constant luminance
    ICtCp = 14       // ICtCp (Rec. ITU-R BT.2100)
}

/// <summary>
/// Color range defining whether luma/chroma use full range or limited/video range
/// </summary>
public enum ColorRange
{
    Unspecified = 0,
    Limited = 1,     // Video range (16-235 for 8-bit)
    Full = 2         // Full range (0-255 for 8-bit)
}

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
