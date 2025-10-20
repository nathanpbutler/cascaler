using cascaler.Models;
using ImageMagick;

namespace cascaler.Services.Interfaces;

/// <summary>
/// Service for extracting and converting video frames for processing.
/// </summary>
public interface IVideoProcessingService
{
    /// <summary>
    /// Extracts frames from a video file as RGB24 data, optionally limited to a frame range.
    /// </summary>
    /// <param name="videoPath">Path to the video file</param>
    /// <param name="startFrame">Optional start frame index (inclusive)</param>
    /// <param name="endFrame">Optional end frame index (exclusive)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<List<VideoFrame>> ExtractFramesAsync(
        string videoPath,
        int? startFrame = null,
        int? endFrame = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a video frame to a MagickImage for processing.
    /// </summary>
    Task<MagickImage?> ConvertFrameToMagickImageAsync(VideoFrame frame);

    /// <summary>
    /// Gets video metadata including frame rate, total frames, and duration.
    /// </summary>
    /// <param name="videoPath">Path to the video file</param>
    /// <returns>Tuple containing (frameRate, totalFrames, duration)</returns>
    Task<(double frameRate, int totalFrames, TimeSpan duration)?> GetVideoInfoAsync(string videoPath);

    /// <summary>
    /// Calculates the frame range based on time parameters.
    /// </summary>
    /// <param name="videoPath">Path to the video file</param>
    /// <param name="startTime">Start time in seconds (optional)</param>
    /// <param name="endTime">End time in seconds (optional)</param>
    /// <param name="duration">Duration in seconds (optional)</param>
    /// <returns>Tuple containing (startFrame, endFrame), or null if video info unavailable</returns>
    Task<(int startFrame, int endFrame)?> CalculateFrameRangeAsync(
        string videoPath,
        double? startTime = null,
        double? endTime = null,
        double? duration = null);
}
