using System.Diagnostics;
using System.Text;
using cascaler.Infrastructure;
using cascaler.Services.Interfaces;
using cascaler.Utilities;
using ImageMagick;

namespace cascaler.Services;

/// <summary>
/// Handles video compilation using FFmpeg with streaming frame input.
/// </summary>
public class VideoCompilationService : IVideoCompilationService
{
    private readonly FFmpegConfiguration _ffmpegConfig;

    public VideoCompilationService(FFmpegConfiguration ffmpegConfig)
    {
        _ffmpegConfig = ffmpegConfig;
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
                       $"-c:v {Constants.DefaultVideoCodec} -crf {Constants.DefaultVideoCRF} " +
                       $"-preset {Constants.DefaultVideoPreset} -pix_fmt {Constants.DefaultVideoPixelFormat} " +
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
