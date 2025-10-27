using ImageMagick;
using nathanbutlerDEV.cascaler.Models;

namespace nathanbutlerDEV.cascaler.Services.Interfaces;

/// <summary>
/// Handles video compilation from streaming frames using FFmpeg.
/// </summary>
public interface IVideoCompilationService
{
    /// <summary>
    /// Starts a unified streaming encoder that handles both video and audio in a single pass.
    /// This replaces the 3-stage process (extract audio → encode video → merge) with a single operation.
    /// </summary>
    /// <param name="sourceVideoPath">Path to source video for audio extraction (null for video-only encoding).</param>
    /// <param name="outputVideoPath">Path for the output video file.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="fps">Frame rate.</param>
    /// <param name="totalFrames">Total number of frames expected.</param>
    /// <param name="options">Processing options including video encoding settings.</param>
    /// <param name="startTime">Optional start time in seconds for audio trimming.</param>
    /// <param name="duration">Optional duration in seconds for audio trimming.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Action to submit frames and a completion task.</returns>
    Task<(Func<int, MagickImage, Task> submitFrame, Task encodingComplete)> StartStreamingEncoderWithAudioAsync(
        string? sourceVideoPath,
        string outputVideoPath,
        int width,
        int height,
        double fps,
        int totalFrames,
        ProcessingOptions options,
        double? startTime = null,
        double? duration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines the appropriate output container format based on audio codec.
    /// </summary>
    /// <param name="audioPath">Path to the audio file.</param>
    /// <returns>Recommended extension (.mp4 or .mkv).</returns>
    Task<string> DetermineOutputContainerAsync(string? audioPath);
}
