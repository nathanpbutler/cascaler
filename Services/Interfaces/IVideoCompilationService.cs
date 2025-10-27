using ImageMagick;

namespace nathanbutlerDEV.cascaler.Services.Interfaces;

/// <summary>
/// Handles video compilation from streaming frames using FFmpeg.
/// </summary>
public interface IVideoCompilationService
{
    /// <summary>
    /// Extracts the audio track from a video file.
    /// </summary>
    /// <param name="videoPath">Path to the source video file.</param>
    /// <param name="outputAudioPath">Path where the audio file should be saved.</param>
    /// <param name="startTime">Optional start time in seconds for audio trimming.</param>
    /// <param name="duration">Optional duration in seconds for audio trimming.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if audio was successfully extracted, false otherwise.</returns>
    Task<bool> ExtractAudioFromVideoAsync(
        string videoPath,
        string outputAudioPath,
        double? startTime = null,
        double? duration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a streaming video encoder process that accepts frames via a channel.
    /// </summary>
    /// <param name="outputVideoPath">Path for the output video file (without audio).</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <param name="fps">Frame rate.</param>
    /// <param name="totalFrames">Total number of frames expected.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Action to accept frames: (frameIndex, MagickImage) => Task, and a completion task.</returns>
    Task<(Func<int, MagickImage, Task> submitFrame, Task encodingComplete)> StartStreamingEncoderAsync(
        string outputVideoPath,
        int width,
        int height,
        double fps,
        int totalFrames,
        CancellationToken cancellationToken = default);

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
        double? startTime = null,
        double? duration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges a video file with an audio file.
    /// </summary>
    /// <param name="videoPath">Path to the video file (no audio).</param>
    /// <param name="audioPath">Path to the audio file.</param>
    /// <param name="outputPath">Path for the final merged video.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if merge succeeded, false otherwise.</returns>
    Task<bool> MergeVideoWithAudioAsync(
        string videoPath,
        string audioPath,
        string outputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines the appropriate output container format based on audio codec.
    /// </summary>
    /// <param name="audioPath">Path to the audio file.</param>
    /// <returns>Recommended extension (.mp4 or .mkv).</returns>
    Task<string> DetermineOutputContainerAsync(string? audioPath);

    /// <summary>
    /// Determines the appropriate output container format based on video's audio codec using FFMediaToolkit.
    /// This replaces FFmpeg subprocess calls with direct codec inspection.
    /// </summary>
    /// <param name="videoPath">Path to the source video file.</param>
    /// <returns>Recommended extension (.mp4 or .mkv).</returns>
    Task<string> DetermineOutputContainerFromVideoAsync(string? videoPath);
}
