using cascaler.Infrastructure;
using cascaler.Models;
using cascaler.Services.Interfaces;
using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;
using ImageMagick;

namespace cascaler.Services;

/// <summary>
/// Handles all video-related operations including frame extraction and conversion.
/// </summary>
public class VideoProcessingService : IVideoProcessingService
{
    private readonly FFmpegConfiguration _ffmpegConfig;

    public VideoProcessingService(FFmpegConfiguration ffmpegConfig)
    {
        _ffmpegConfig = ffmpegConfig;
    }

    public async Task<List<VideoFrame>> ExtractFramesAsync(
        string videoPath,
        int? startFrame = null,
        int? endFrame = null,
        CancellationToken cancellationToken = default)
    {
        var frames = new List<VideoFrame>();

        try
        {
            _ffmpegConfig.Initialize();

            // Configure media options for RGB24 output format
            var mediaOptions = new MediaOptions
            {
                VideoPixelFormat = ImagePixelFormat.Rgb24
            };

            using var mediaFile = MediaFile.Open(videoPath, mediaOptions);

            if (mediaFile.Video == null)
            {
                Console.WriteLine("Error: No video stream found in file");
                return frames;
            }

            var frameCount = mediaFile.Video.Info.NumberOfFrames ?? (int)(mediaFile.Info.Duration.TotalSeconds * mediaFile.Video.Info.AvgFrameRate);
            var frameRate = mediaFile.Video.Info.AvgFrameRate;

            // Determine frame range
            int actualStartFrame = startFrame ?? 0;
            int actualEndFrame = endFrame ?? frameCount;

            // Clamp to valid range
            actualStartFrame = Math.Max(0, actualStartFrame);
            actualEndFrame = Math.Min(frameCount, actualEndFrame);

            for (int i = actualStartFrame; i < actualEndFrame; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var timestamp = TimeSpan.FromSeconds((double)i / frameRate);
                    var imageData = mediaFile.Video.GetFrame(timestamp);

                    frames.Add(new VideoFrame
                    {
                        Data = imageData.Data.ToArray(),
                        Width = imageData.ImageSize.Width,
                        Height = imageData.ImageSize.Height,
                        PixelFormat = ImagePixelFormat.Rgb24,
                        FrameIndex = i,
                        Timestamp = timestamp,
                        Stride = imageData.Stride
                    });
                }
                catch (Exception frameEx)
                {
                    Console.WriteLine($"Warning: Failed to extract frame {i}: {frameEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting frames from video: {ex.Message}");
            Console.WriteLine($"Exception type: {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
        }

        return await Task.FromResult(frames);
    }

    public async Task<(double frameRate, int totalFrames, TimeSpan duration)?> GetVideoInfoAsync(string videoPath)
    {
        try
        {
            _ffmpegConfig.Initialize();

            var mediaOptions = new MediaOptions
            {
                VideoPixelFormat = ImagePixelFormat.Rgb24
            };

            using var mediaFile = MediaFile.Open(videoPath, mediaOptions);

            if (mediaFile.Video == null)
            {
                Console.WriteLine("Error: No video stream found in file");
                return null;
            }

            var frameRate = mediaFile.Video.Info.AvgFrameRate;
            var frameCount = mediaFile.Video.Info.NumberOfFrames ?? (int)(mediaFile.Info.Duration.TotalSeconds * frameRate);
            var duration = mediaFile.Info.Duration;

            return await Task.FromResult<(double, int, TimeSpan)?>((frameRate, frameCount, duration));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading video info: {ex.Message}");
            return null;
        }
    }

    public async Task<(int startFrame, int endFrame)?> CalculateFrameRangeAsync(
        string videoPath,
        double? startTime = null,
        double? endTime = null,
        double? duration = null)
    {
        var videoInfo = await GetVideoInfoAsync(videoPath);
        if (!videoInfo.HasValue)
            return null;

        var (frameRate, totalFrames, videoDuration) = videoInfo.Value;

        // Calculate frame indices from time values
        int startFrame = startTime.HasValue ? (int)(startTime.Value * frameRate) : 0;
        int endFrame;

        if (endTime.HasValue)
        {
            endFrame = (int)(endTime.Value * frameRate);
        }
        else if (duration.HasValue)
        {
            endFrame = startFrame + (int)(duration.Value * frameRate);
        }
        else
        {
            endFrame = totalFrames;
        }

        // Clamp to valid range
        startFrame = Math.Max(0, Math.Min(startFrame, totalFrames));
        endFrame = Math.Max(startFrame, Math.Min(endFrame, totalFrames));

        return (startFrame, endFrame);
    }

    public async Task<MagickImage?> ConvertFrameToMagickImageAsync(VideoFrame frame)
    {
        try
        {
            return await Task.Run(() =>
            {
                // Handle RGB24 format specifically since we configured FFMediaToolkit to output RGB24
                if (frame.PixelFormat == ImagePixelFormat.Rgb24)
                {
                    var expectedRGB24 = frame.Width * frame.Height * 3;
                    var rowWidth = frame.Width * 3;

                    byte[] rgbData;

                    if (frame.Data.Length == expectedRGB24)
                    {
                        // Perfect match - RGB24 format, 3 bytes per pixel, no padding
                        rgbData = frame.Data;
                    }
                    else if (frame.Data.Length > expectedRGB24)
                    {
                        // Data buffer is larger than needed - extract only the needed data
                        rgbData = ExtractCleanRGB24Data(frame.Data, frame.Width, frame.Height, frame.Stride);
                    }
                    else
                    {
                        return null;
                    }

                    // Create MagickImage and use ReadPixels for raw RGB data
                    var image = new MagickImage(MagickColors.Transparent, (uint)frame.Width, (uint)frame.Height);
                    var settings = new PixelReadSettings((uint)frame.Width, (uint)frame.Height, StorageType.Char, PixelMapping.RGB);
                    image.ReadPixels(rgbData, settings);

                    return image;
                }
                else
                {
                    // Fallback for other formats (shouldn't happen with our RGB24 configuration)
                    return null;
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Frame conversion failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts clean RGB24 data from a buffer with padding/stride.
    /// </summary>
    private byte[] ExtractCleanRGB24Data(byte[] sourceData, int width, int height, int stride)
    {
        var expectedSize = width * height * 3;
        var cleanData = new byte[expectedSize];

        // Use the provided stride (bytes per row including padding)
        var bytesPerPixel = 3;
        var rowWidth = width * bytesPerPixel;

        // Validate that stride is at least as large as rowWidth
        if (stride < rowWidth)
        {
            // Fallback: try to copy what we can
            var copySize = Math.Min(expectedSize, sourceData.Length);
            Array.Copy(sourceData, cleanData, copySize);
            return cleanData;
        }

        if (stride == rowWidth)
        {
            // No padding, can copy directly
            Array.Copy(sourceData, cleanData, expectedSize);
        }
        else
        {
            // Has padding, copy row by row
            for (int y = 0; y < height; y++)
            {
                var sourceOffset = y * stride;
                var destOffset = y * rowWidth;

                // Safety check to prevent buffer overflow
                if (sourceOffset + rowWidth > sourceData.Length)
                {
                    break;
                }

                Array.Copy(sourceData, sourceOffset, cleanData, destOffset, rowWidth);
            }
        }

        return cleanData;
    }
}
