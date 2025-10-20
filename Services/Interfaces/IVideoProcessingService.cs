using cascaler.Models;
using ImageMagick;

namespace cascaler.Services.Interfaces;

/// <summary>
/// Service for extracting and converting video frames for processing.
/// </summary>
public interface IVideoProcessingService
{
    /// <summary>
    /// Extracts all frames from a video file as RGB24 data.
    /// </summary>
    Task<List<VideoFrame>> ExtractFramesAsync(string videoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a video frame to a MagickImage for processing.
    /// </summary>
    Task<MagickImage?> ConvertFrameToMagickImageAsync(VideoFrame frame);
}
