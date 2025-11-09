using nathanbutlerDEV.cascaler.Infrastructure;

namespace nathanbutlerDEV.cascaler.Models;

public enum ImagePixelFormat
{
    Rgb24,   // 8-bit RGB (3 bytes per pixel)
    Rgb48    // 16-bit RGB (6 bytes per pixel, for HDR)
}

public class VideoFrame
{
    public byte[] Data { get; set; } = [];
    public int Width { get; set; }
    public int Height { get; set; }
    public ImagePixelFormat PixelFormat { get; set; }
    public int FrameIndex { get; set; }
    public TimeSpan Timestamp { get; set; }
    public int Stride { get; set; } // Bytes per row including padding

    // Color metadata for HDR/wide gamut support
    public ColorPrimaries ColorPrimaries { get; set; } = ColorPrimaries.Unspecified;
    public TransferCharacteristic TransferCharacteristic { get; set; } = TransferCharacteristic.Unspecified;
    public YuvColorSpace ColorSpace { get; set; } = YuvColorSpace.Unspecified;
    public ColorRange ColorRange { get; set; } = ColorRange.Unspecified;
    public int BitDepth { get; set; } = 8; // 8, 10, or 12 bits per channel

    /// <summary>
    /// Determines if this frame contains HDR content based on transfer characteristic
    /// </summary>
    public bool IsHDR()
    {
        return TransferCharacteristic is
            TransferCharacteristic.PQ or
            TransferCharacteristic.HLG or
            TransferCharacteristic.SMPTE428;
    }
}
