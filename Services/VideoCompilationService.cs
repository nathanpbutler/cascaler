using System.Diagnostics;
using System.Text;
using FFMediaToolkit;
using FFMediaToolkit.Audio;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Encoding;
using FFMediaToolkit.Graphics;
using ImageMagick;
using Microsoft.Extensions.Options;
using nathanbutlerDEV.cascaler.Infrastructure;
using nathanbutlerDEV.cascaler.Infrastructure.Options;
using nathanbutlerDEV.cascaler.Services.Interfaces;
using nathanbutlerDEV.cascaler.Utilities;

namespace nathanbutlerDEV.cascaler.Services;

/// <summary>
/// Wrapper for audio frame data that can be stored in collections.
/// AudioData is a ref struct and cannot be stored in tuples/lists directly.
/// Stores the underlying float[][] sample data.
/// </summary>
internal class AudioFrameData
{
    public float[][] SampleData { get; set; }
    public TimeSpan Timestamp { get; set; }

    public AudioFrameData(float[][] sampleData, TimeSpan timestamp)
    {
        SampleData = sampleData;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Handles video compilation using FFmpeg with streaming frame input.
/// </summary>
public class VideoCompilationService : IVideoCompilationService
{
    private readonly FFmpegConfiguration _ffmpegConfig;
    private readonly VideoEncodingOptions _encodingOptions;

    public VideoCompilationService(FFmpegConfiguration ffmpegConfig, IOptions<VideoEncodingOptions> encodingOptions)
    {
        _ffmpegConfig = ffmpegConfig;
        _encodingOptions = encodingOptions.Value;
    }

    public async Task<bool> ExtractAudioFromVideoAsync(
        string videoPath,
        string outputAudioPath,
        double? startTime = null,
        double? duration = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if video has audio stream first
            var hasAudio = await VideoHasAudioStreamAsync(videoPath, cancellationToken);
            if (!hasAudio)
            {
                return false;
            }

            // Build FFmpeg arguments with optional trimming
            // Use -ss before -i for faster seeking (input seeking)
            var argumentsBuilder = new System.Text.StringBuilder();

            if (startTime.HasValue)
            {
                argumentsBuilder.Append($"-ss {startTime.Value:F3} ");
            }

            argumentsBuilder.Append($"-i \"{videoPath}\" ");

            if (duration.HasValue)
            {
                argumentsBuilder.Append($"-t {duration.Value:F3} ");
            }

            argumentsBuilder.Append($"-vn -acodec aac -b:a 256k \"{outputAudioPath}\" -y");

            var arguments = argumentsBuilder.ToString();
            Console.WriteLine($"[DEBUG] FFmpeg audio extraction command:");
            Console.WriteLine($"  ffmpeg {arguments}");

            var result = await RunFFmpegAsync(arguments, "Extracting audio", cancellationToken);

            Console.WriteLine($"[DEBUG] FFmpeg extraction result: {result}");
            Console.WriteLine($"[DEBUG] Output file exists: {File.Exists(outputAudioPath)}");
            if (File.Exists(outputAudioPath))
            {
                Console.WriteLine($"[DEBUG] Output file size: {new FileInfo(outputAudioPath).Length} bytes");
            }

            if (result && File.Exists(outputAudioPath))
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting audio: {ex.Message}");
            return false;
        }
    }

    public async Task<(Func<int, MagickImage, Task> submitFrame, Task encodingComplete)> StartStreamingEncoderAsync(
        string outputVideoPath,
        int width,
        int height,
        double fps,
        int totalFrames,
        CancellationToken cancellationToken = default)
    {
        // Create frame ordering buffer
        var frameBuffer = new FrameOrderingBuffer(totalFrames);

        // Start FFmpeg process
        var ffmpegProcess = StartFFmpegEncoderProcess(outputVideoPath, width, height, fps);

        // Start the frame streaming task
        var streamingTask = StreamFramesToFFmpegAsync(
            ffmpegProcess,
            frameBuffer,
            width,
            height,
            totalFrames,
            cancellationToken);

        // Return submit function and completion task
        Func<int, MagickImage, Task> submitFrame = async (frameIndex, frame) =>
        {
            await frameBuffer.AddFrameAsync(frameIndex, frame);
        };

        return (submitFrame, streamingTask);
    }

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
    public async Task<(Func<int, MagickImage, Task> submitFrame, Task encodingComplete)> StartStreamingEncoderWithAudioAsync(
        string? sourceVideoPath,
        string outputVideoPath,
        int width,
        int height,
        double fps,
        int totalFrames,
        double? startTime = null,
        double? duration = null,
        CancellationToken cancellationToken = default)
    {
        // Create frame ordering buffer
        var frameBuffer = new FrameOrderingBuffer(totalFrames);

        // Extract audio frames if source video provided
        List<AudioFrameData>? audioFrames = null;
        AudioEncoderSettings? audioSettings = null;

        if (!string.IsNullOrEmpty(sourceVideoPath))
        {
            audioFrames = await ExtractAudioFramesAsync(sourceVideoPath, startTime, duration, cancellationToken);

            if (audioFrames != null && audioFrames.Count > 0)
            {
                // Get audio info from source video to configure encoder
                _ffmpegConfig.Initialize();
                var mediaOptions = new MediaOptions { StreamsToLoad = MediaMode.Audio };
                using var sourceMedia = MediaFile.Open(sourceVideoPath, mediaOptions);

                if (sourceMedia.Audio != null)
                {
                    var audioInfo = sourceMedia.Audio.Info;

                    Console.WriteLine($"[DEBUG] Audio info - SampleRate: {audioInfo.SampleRate}, Channels: {audioInfo.NumChannels}, SamplesPerFrame: {audioInfo.SamplesPerFrame}, SampleFormat: {audioInfo.SampleFormat}");

                    audioSettings = new AudioEncoderSettings(
                        audioInfo.SampleRate,
                        audioInfo.NumChannels,
                        AudioCodec.AAC
                    )
                    {
                        SampleFormat = SampleFormat.SingleP, // AAC requires floating-point planar (fltp)
                        SamplesPerFrame = 1024, // AAC standard frame size (not source frame size)
                        Bitrate = 256_000 // 256 kbps
                    };

                    Console.WriteLine($"[DEBUG] AAC encoder configured - SamplesPerFrame: 1024 (AAC standard, source had {audioInfo.SamplesPerFrame})");
                }
            }
        }

        // Start the unified encoding task
        var encodingTask = StreamFramesToMediaBuilderAsync(
            outputVideoPath,
            frameBuffer,
            width,
            height,
            fps,
            totalFrames,
            audioFrames,
            audioSettings,
            cancellationToken);

        // Return submit function and completion task
        Func<int, MagickImage, Task> submitFrame = async (frameIndex, frame) =>
        {
            await frameBuffer.AddFrameAsync(frameIndex, frame);
        };

        return (submitFrame, encodingTask);
    }

    public async Task<bool> MergeVideoWithAudioAsync(
        string videoPath,
        string audioPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine if we need to transcode audio for MP4 compatibility
            var outputExt = Path.GetExtension(outputPath).ToLowerInvariant();
            var audioCodec = outputExt == ".mp4" ? "aac" : "copy";

            var arguments = $"-i \"{videoPath}\" -i \"{audioPath}\" -c:v copy -c:a {audioCodec} -shortest \"{outputPath}\" -y";
            var result = await RunFFmpegAsync(arguments, "Merging video and audio", cancellationToken);

            if (result && File.Exists(outputPath))
            {
                Console.WriteLine($"Video with audio saved to: {outputPath}");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error merging video and audio: {ex.Message}");
            return false;
        }
    }

    public async Task<string> DetermineOutputContainerAsync(string? audioPath)
    {
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            // No audio, default to MP4
            return ".mp4";
        }

        try
        {
            // Probe audio codec
            var arguments = $"-i \"{audioPath}\" -hide_banner";
            var (output, _) = await RunFFmpegWithOutputAsync(arguments, CancellationToken.None);

            // Parse codec from output
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("Audio:"))
                {
                    // Extract codec name (e.g., "Audio: aac", "Audio: opus")
                    var parts = line.Split(new[] { "Audio:", "," }, StringSplitOptions.TrimEntries);
                    if (parts.Length > 1)
                    {
                        var codecName = parts[1].Split(' ')[0].Trim();

                        // Check if codec is MP4 compatible
                        if (Constants.MP4CompatibleAudioCodecs.Contains(codecName))
                        {
                            return ".mp4";
                        }
                        else
                        {
                            Console.WriteLine($"Audio codec '{codecName}' not MP4-compatible, using MKV container");
                            return ".mkv";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error determining audio codec: {ex.Message}");
        }

        // Default to MP4
        return ".mp4";
    }

    /// <summary>
    /// Determines the appropriate output container format based on video's audio codec using FFMediaToolkit.
    /// This replaces FFmpeg subprocess calls with direct codec inspection.
    /// </summary>
    /// <param name="videoPath">Path to the source video file.</param>
    /// <returns>Recommended extension (.mp4 or .mkv).</returns>
    public async Task<string> DetermineOutputContainerFromVideoAsync(string? videoPath)
    {
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            // No video, default to MP4
            return ".mp4";
        }

        try
        {
            _ffmpegConfig.Initialize();

            // Open video file with audio-only mode
            var mediaOptions = new MediaOptions
            {
                StreamsToLoad = MediaMode.Audio
            };

            using var mediaFile = MediaFile.Open(videoPath, mediaOptions);

            if (mediaFile.Audio == null)
            {
                // No audio stream, default to MP4
                return ".mp4";
            }

            var codecId = mediaFile.Audio.Info.CodecId;
            var codecName = codecId.ToString().ToLowerInvariant();

            // Check if codec is MP4 compatible
            if (Constants.MP4CompatibleAudioCodecs.Contains(codecName))
            {
                return ".mp4";
            }
            else
            {
                Console.WriteLine($"Audio codec '{codecName}' not MP4-compatible, using MKV container");
                return ".mkv";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error determining audio codec from video: {ex.Message}");
            // Default to MP4
            return ".mp4";
        }
    }

    private async Task<bool> VideoHasAudioStreamAsync(string videoPath, CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine($"[DEBUG] Checking for audio stream in: {Path.GetFileName(videoPath)}");
            var arguments = $"-i \"{videoPath}\" -hide_banner";
            var (output, error) = await RunFFmpegWithOutputAsync(arguments, cancellationToken);

            // Check if output contains "Audio:" stream (FFmpeg outputs stream info to stderr)
            var hasAudio = error.Contains("Audio:", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"[DEBUG] VideoHasAudioStreamAsync result: {hasAudio}");

            if (!hasAudio)
            {
                Console.WriteLine($"[DEBUG] FFmpeg stderr (first 500 chars):");
                Console.WriteLine($"  {error.Substring(0, Math.Min(500, error.Length))}");
            }

            return hasAudio;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] VideoHasAudioStreamAsync exception: {ex.Message}");
            return false;
        }
    }

    private Process StartFFmpegEncoderProcess(string outputVideoPath, int width, int height, double fps)
    {
        var arguments = $"-f rawvideo -pix_fmt rgb24 -s {width}x{height} -r {fps} -i pipe:0 " +
                       $"-c:v {_encodingOptions.DefaultCodec} -crf {_encodingOptions.DefaultCRF} " +
                       $"-preset {_encodingOptions.DefaultPreset} -pix_fmt {_encodingOptions.DefaultPixelFormat} " +
                       $"\"{outputVideoPath}\" -y";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = processStartInfo };
        process.Start();

        // Read stderr in background to prevent buffer deadlock
        _ = Task.Run(() =>
        {
            while (!process.StandardError.EndOfStream)
            {
                process.StandardError.ReadLine();
            }
        });

        return process;
    }

    private async Task StreamFramesToFFmpegAsync(
        Process ffmpegProcess,
        FrameOrderingBuffer frameBuffer,
        int width,
        int height,
        int totalFrames,
        CancellationToken cancellationToken)
    {
        try
        {
            var stdin = ffmpegProcess.StandardInput.BaseStream;
            int framesWritten = 0;

            while (framesWritten < totalFrames && !cancellationToken.IsCancellationRequested)
            {
                // Try to get the next sequential frame
                var result = await frameBuffer.TryGetNextFrameAsync();

                if (result == null)
                {
                    // No frame ready yet, wait a bit
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                var (frameIndex, frame) = result.Value;
                if (frame == null)
                    break;

                using (frame)
                {
                    // Convert MagickImage to raw RGB24 bytes
                    var rgb24Data = ConvertToRGB24(frame, width, height);

                    // Write to FFmpeg stdin
                    await stdin.WriteAsync(rgb24Data, 0, rgb24Data.Length, cancellationToken);
                    framesWritten++;
                }
            }

            // Close stdin to signal end of input
            stdin.Close();

            // Wait for FFmpeg to finish encoding
            await ffmpegProcess.WaitForExitAsync(cancellationToken);

            if (ffmpegProcess.ExitCode == 0)
            {
                Console.WriteLine($"Video encoding completed: {framesWritten} frames written");
            }
            else
            {
                Console.WriteLine($"Warning: FFmpeg exited with code {ffmpegProcess.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error streaming frames to FFmpeg: {ex.Message}");
            throw;
        }
        finally
        {
            await frameBuffer.CompleteAsync();

            if (!ffmpegProcess.HasExited)
            {
                ffmpegProcess.Kill();
            }
            ffmpegProcess.Dispose();
        }
    }

    /// <summary>
    /// Splits source audio frames into AAC-sized chunks (1024 samples per frame).
    /// This is necessary because source audio may have different frame sizes (e.g., 2048 samples)
    /// but AAC encoder requires exactly 1024 samples per frame.
    /// Recalculates timestamps to ensure perfect chronological ordering.
    /// </summary>
    private List<AudioFrameData> SplitAudioFramesToAacFrameSize(
        List<AudioFrameData> sourceFrames,
        int aacFrameSize,
        int sampleRate)
    {
        var result = new List<AudioFrameData>();

        if (sourceFrames.Count == 0)
            return result;

        // Start from the first source frame's timestamp
        var startTimestamp = sourceFrames[0].Timestamp;
        int totalSamplesProcessed = 0;

        foreach (var sourceFrame in sourceFrames)
        {
            var samplesPerChannel = sourceFrame.SampleData[0].Length;
            var channels = sourceFrame.SampleData.Length;

            // Split the source frame into chunks of aacFrameSize
            for (int offset = 0; offset < samplesPerChannel; offset += aacFrameSize)
            {
                var remainingSamples = Math.Min(aacFrameSize, samplesPerChannel - offset);
                var chunkData = new float[channels][];

                for (int ch = 0; ch < channels; ch++)
                {
                    chunkData[ch] = new float[remainingSamples];
                    Array.Copy(sourceFrame.SampleData[ch], offset, chunkData[ch], 0, remainingSamples);
                }

                // Calculate timestamp based on total samples processed to ensure perfect ordering
                // This prevents any rounding errors or ordering issues from source timestamps
                var chunkTimestamp = startTimestamp + TimeSpan.FromSeconds((double)totalSamplesProcessed / sampleRate);
                totalSamplesProcessed += remainingSamples;

                result.Add(new AudioFrameData(chunkData, chunkTimestamp));
            }
        }

        Console.WriteLine($"[DEBUG] Audio splitting complete - first frame: {result[0].Timestamp}, last frame: {result[result.Count - 1].Timestamp}");
        return result;
    }

    /// <summary>
    /// Streams video frames (and optionally audio) to FFMediaToolkit's MediaBuilder for encoding.
    /// This replaces direct FFmpeg subprocess calls with the FFMediaToolkit API.
    /// </summary>
    private async Task StreamFramesToMediaBuilderAsync(
        string outputVideoPath,
        FrameOrderingBuffer frameBuffer,
        int width,
        int height,
        double fps,
        int totalFrames,
        List<AudioFrameData>? audioFrames,
        AudioEncoderSettings? audioSettings,
        CancellationToken cancellationToken)
    {
        MediaOutput? outputFile = null;

        try
        {
            _ffmpegConfig.Initialize();

            // Resolve to absolute path and ensure directory exists
            var absoluteOutputPath = Path.GetFullPath(outputVideoPath);
            var outputDirectory = Path.GetDirectoryName(absoluteOutputPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            // Split source audio frames into AAC-sized frames (1024 samples)
            // This is necessary because source audio may have larger frame sizes (e.g., 2048)
            // but AAC encoder requires exactly 1024 samples per frame
            if (audioFrames != null && audioSettings != null)
            {
                var originalCount = audioFrames.Count;
                var originalSamplesPerFrame = audioFrames.Count > 0 ? audioFrames[0].SampleData[0].Length : 0;

                Console.WriteLine($"[DEBUG] Splitting {originalCount} source audio frames ({originalSamplesPerFrame} samples each) into AAC frames (1024 samples each)");
                audioFrames = SplitAudioFramesToAacFrameSize(audioFrames, 1024, audioSettings.SampleRate);
                Console.WriteLine($"[DEBUG] After splitting: {audioFrames.Count} AAC audio frames");
            }

            // Configure video encoder settings
            var videoSettings = new VideoEncoderSettings(width, height, (int)Math.Round(fps), VideoCodec.H264)
            {
                EncoderPreset = EncoderPreset.Fast,
                CRF = _encodingOptions.DefaultCRF
            };

            // Create output container with video stream (use absolute path)
            var builder = MediaBuilder
                .CreateContainer(absoluteOutputPath)
                .WithVideo(videoSettings);

            // Add audio stream if available
            if (audioSettings != null)
            {
                builder = builder.WithAudio(audioSettings);
            }

            outputFile = builder.Create();

            int framesWritten = 0;
            int audioFrameIndex = 0;

            // Calculate time per frame for audio synchronization
            var timePerFrame = TimeSpan.FromSeconds(1.0 / fps);

            while (framesWritten < totalFrames && !cancellationToken.IsCancellationRequested)
            {
                // Try to get the next sequential frame
                var result = await frameBuffer.TryGetNextFrameAsync();

                if (result == null)
                {
                    // No frame ready yet, wait a bit
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                var (frameIndex, frame) = result.Value;
                if (frame == null)
                    break;

                using (frame)
                {
                    try
                    {
                        // Convert MagickImage to ImageData and add to video stream
                        var imageData = ConvertMagickImageToImageData(frame);
                        outputFile.Video.AddFrame(imageData);
                        framesWritten++;

                        // Add corresponding audio frames if available
                        if (audioFrames != null && outputFile.Audio != null)
                        {
                            var currentVideoTime = timePerFrame * framesWritten;

                            // Add all audio frames that should be written before the next video frame
                            while (audioFrameIndex < audioFrames.Count)
                            {
                                var audioFrameData = audioFrames[audioFrameIndex];

                                if (audioFrameData.Timestamp <= currentVideoTime)
                                {
                                    // Validate audio frame data
                                    if (audioFrameData.SampleData == null || audioFrameData.SampleData.Length == 0)
                                    {
                                        Console.WriteLine($"[WARNING] Audio frame {audioFrameIndex} has no data, skipping");
                                        audioFrameIndex++;
                                        continue;
                                    }

                                    try
                                    {
                                        // Use the float[][] overload of AddFrame
                                        outputFile.Audio.AddFrame(audioFrameData.SampleData, audioFrameData.Timestamp);
                                        audioFrameIndex++;
                                    }
                                    catch (Exception audioEx)
                                    {
                                        Console.WriteLine($"[ERROR] Failed to add audio frame {audioFrameIndex}: {audioEx.Message}");
                                        Console.WriteLine($"  Channels: {audioFrameData.SampleData.Length}, Samples per channel: {audioFrameData.SampleData[0]?.Length ?? 0}");
                                        Console.WriteLine($"  Timestamp: {audioFrameData.Timestamp}");
                                        throw;
                                    }
                                }
                                else
                                {
                                    break; // Audio frame is ahead of video, wait for next video frame
                                }
                            }
                        }
                    }
                    catch (Exception frameEx)
                    {
                        Console.WriteLine($"[ERROR] Failed to process frame {frameIndex}: {frameEx.Message}");
                        throw;
                    }
                }
            }

            // Add any remaining audio frames
            if (audioFrames != null && outputFile.Audio != null)
            {
                while (audioFrameIndex < audioFrames.Count)
                {
                    var audioFrameData = audioFrames[audioFrameIndex];
                    outputFile.Audio.AddFrame(audioFrameData.SampleData, audioFrameData.Timestamp);
                    audioFrameIndex++;
                }
            }

            Console.WriteLine($"Video encoding completed: {framesWritten} frames written");
            if (audioFrames != null)
            {
                Console.WriteLine($"Audio encoding completed: {audioFrameIndex} audio frames written");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error streaming frames to MediaBuilder: {ex.Message}");
            throw;
        }
        finally
        {
            await frameBuffer.CompleteAsync();
            outputFile?.Dispose();
        }
    }

    private byte[] ConvertToRGB24(MagickImage image, int expectedWidth, int expectedHeight)
    {
        // Ensure image dimensions match expected dimensions
        if (image.Width != expectedWidth || image.Height != expectedHeight)
        {
            throw new InvalidOperationException(
                $"Frame dimensions {image.Width}x{image.Height} do not match expected {expectedWidth}x{expectedHeight}");
        }

        // Export as raw RGB24 bytes (3 bytes per pixel: R, G, B)
        var pixels = image.GetPixels();
        var rgb24Data = pixels.ToByteArray(PixelMapping.RGB);

        if (rgb24Data == null)
        {
            throw new InvalidOperationException("Failed to convert image pixels to byte array");
        }

        return rgb24Data;
    }

    /// <summary>
    /// Converts a MagickImage to FFMediaToolkit's ImageData format for video encoding.
    /// </summary>
    private ImageData ConvertMagickImageToImageData(MagickImage image)
    {
        // Export as raw RGB24 bytes (3 bytes per pixel: R, G, B)
        var pixels = image.GetPixels();
        var rgb24Data = pixels.ToByteArray(PixelMapping.RGB);

        if (rgb24Data == null)
        {
            throw new InvalidOperationException("Failed to convert image pixels to byte array");
        }

        return new ImageData(
            rgb24Data,
            ImagePixelFormat.Rgb24,
            (int)image.Width,
            (int)image.Height
        );
    }

    /// <summary>
    /// Extracts and buffers audio frames from a source video with optional trimming.
    /// </summary>
    /// <param name="videoPath">Path to the source video file.</param>
    /// <param name="startTime">Optional start time in seconds for audio trimming.</param>
    /// <param name="duration">Optional duration in seconds for audio trimming.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of audio frames with timestamps, or null if no audio stream exists.</returns>
    private async Task<List<AudioFrameData>?> ExtractAudioFramesAsync(
        string videoPath,
        double? startTime = null,
        double? duration = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ffmpegConfig.Initialize();

            // Open video file with audio-only mode to avoid memory issues
            var mediaOptions = new MediaOptions
            {
                StreamsToLoad = MediaMode.Audio
            };

            using var mediaFile = MediaFile.Open(videoPath, mediaOptions);

            if (mediaFile.Audio == null)
            {
                Console.WriteLine("No audio stream found in video");
                return null;
            }

            var audioInfo = mediaFile.Audio.Info;
            var audioFrames = new List<AudioFrameData>();
            var startTimeSpan = startTime.HasValue ? TimeSpan.FromSeconds(startTime.Value) : TimeSpan.Zero;
            var endTimeSpan = duration.HasValue
                ? TimeSpan.FromSeconds(startTime.GetValueOrDefault() + duration.Value)
                : TimeSpan.MaxValue;

            Console.WriteLine($"[DEBUG] Extracting audio frames - SampleRate: {audioInfo.SampleRate}, Channels: {audioInfo.NumChannels}");

            // Read all audio frames
            while (mediaFile.Audio.TryGetNextFrame(out AudioData frame))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var timestamp = mediaFile.Audio.Position;

                // Apply trimming filter
                if (timestamp < startTimeSpan)
                    continue;

                if (timestamp > endTimeSpan)
                    break;

                // Adjust timestamp relative to start time
                var adjustedTimestamp = timestamp - startTimeSpan;

                // Copy audio sample data from ref struct to managed memory
                // GetSampleData() returns float[][] (channels x samples)
                var sampleData = frame.GetSampleData();

                if (audioFrames.Count == 0)
                {
                    Console.WriteLine($"[DEBUG] First audio frame - Channels: {sampleData.Length}, Samples: {sampleData[0]?.Length ?? 0}, NumSamples: {frame.NumSamples}");
                }

                audioFrames.Add(new AudioFrameData(
                    sampleData,
                    adjustedTimestamp
                ));
            }

            Console.WriteLine($"[DEBUG] Extracted {audioFrames.Count} audio frames");

            return await Task.FromResult(audioFrames);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting audio frames: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> RunFFmpegAsync(
        string arguments,
        string operationDescription,
        CancellationToken cancellationToken)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            // Read output to prevent buffer deadlock
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"FFmpeg {operationDescription} failed with exit code {process.ExitCode}");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    // Show last few lines of error
                    var errorLines = error.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(5);
                    Console.WriteLine($"FFmpeg error output:");
                    foreach (var line in errorLines)
                    {
                        Console.WriteLine($"  {line.Trim()}");
                    }
                }
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running FFmpeg ({operationDescription}): {ex.Message}");
            return false;
        }
    }

    private async Task<(string output, string error)> RunFFmpegWithOutputAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        return (output, error);
    }
}
