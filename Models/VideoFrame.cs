using FFMediaToolkit.Graphics;

namespace cascaler.Models;

public class VideoFrame
{
    public byte[] Data { get; set; } = [];
    public int Width { get; set; }
    public int Height { get; set; }
    public ImagePixelFormat PixelFormat { get; set; }
    public int FrameIndex { get; set; }
    public TimeSpan Timestamp { get; set; }
    public int Stride { get; set; } // Bytes per row including padding
}
