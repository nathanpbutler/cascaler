using System.Threading.Channels;
using cascaler.Infrastructure;
using cascaler.Models;
using cascaler.Services.Interfaces;
using cascaler.Utilities;
using ImageMagick;
using ShellProgressBar;

namespace cascaler.Services;

/// <summary>
/// Orchestrates batch processing of media files with parallel execution and progress tracking.
/// </summary>
public class MediaProcessor : IMediaProcessor
{
    private readonly IImageProcessingService _imageService;
    private readonly IVideoProcessingService _videoService;
    private readonly IProgressTracker _progressTracker;
    private readonly ProcessingConfiguration _config;

    public MediaProcessor(
        IImageProcessingService imageService,
        IVideoProcessingService videoService,
        IProgressTracker progressTracker,
        ProcessingConfiguration config)
    {
        _imageService = imageService;
        _videoService = videoService;
        _progressTracker = progressTracker;
        _config = config;
    }

    public async Task<List<ProcessingResult>> ProcessMediaFilesAsync(
        List<string> inputFiles,
        ProcessingOptions options,
        CancellationToken cancellationToken = default)
    {
        // Validate that OutputPath is set
        if (string.IsNullOrEmpty(options.OutputPath))
        {
            throw new ArgumentException("OutputPath must be set before processing", nameof(options));
        }

        // Store OutputPath in non-nullable variable for flow analysis
        var outputPath = options.OutputPath;

        var results = new List<ProcessingResult>();
        var progressOptions = new ProgressBarOptions
        {
            ProgressCharacter = Constants.ProgressCharacter,
            ProgressBarOnBottom = Constants.ProgressBarOnBottom,
            ShowEstimatedDuration = Constants.ShowEstimatedDuration,
            DisableBottomPercentage = Constants.DisableBottomPercentage
        };

        // Hide cursor during progress bar operation
        Console.CursorVisible = false;

        using var progressBar = new ProgressBar(inputFiles.Count, "Processing images", progressOptions);

        // Set initial estimated duration
        progressBar.EstimatedDuration = TimeSpan.FromMinutes(Constants.InitialEstimatedDurationMinutes);

        // Timing and progress tracking
        var startTime = DateTime.Now;
        var completedCount = new SharedCounter();

        // Create a channel for work items
        var channel = Channel.CreateUnbounded<string>();

        // Producer task - adds all files to the channel
        var producer = Task.Run(async () =>
        {
            foreach (var file in inputFiles)
            {
                await channel.Writer.WriteAsync(file, cancellationToken);
            }
            channel.Writer.Complete();
        }, cancellationToken);

        // Consumer tasks - process files from the channel
        var semaphore = new SemaphoreSlim(options.MaxThreads, options.MaxThreads);
        var processingTasks = new List<Task<ProcessingResult>>();

        await foreach (var inputFile in channel.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () => await ProcessSingleMediaWrapperAsync(
                inputFile,
                outputPath,
                options,
                progressBar,
                semaphore,
                startTime,
                inputFiles.Count,
                completedCount,
                cancellationToken), cancellationToken);

            processingTasks.Add(task);
        }

        // Wait for all processing to complete
        var allResults = await Task.WhenAll(processingTasks);
        results.AddRange(allResults);

        return results;
    }

    private async Task<ProcessingResult> ProcessSingleMediaWrapperAsync(
        string inputPath,
        string outputPath,
        ProcessingOptions options,
        ProgressBar progressBar,
        SemaphoreSlim semaphore,
        DateTime startTime,
        int totalFiles,
        SharedCounter completedCount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ProcessSingleMediaAsync(
                inputPath,
                outputPath,
                options,
                progressBar,
                cancellationToken);

            // Update progress using the centralized ProgressTracker
            var currentCompleted = completedCount.Increment();
            _progressTracker.UpdateProgress(
                currentCompleted,
                totalFiles,
                startTime,
                progressBar,
                Path.GetFileName(inputPath),
                result.Success);

            return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<ProcessingResult> ProcessSingleMediaAsync(
        string inputPath,
        string outputPath,
        ProcessingOptions options,
        ProgressBar progressBar,
        CancellationToken cancellationToken = default)
    {
        var result = new ProcessingResult { InputPath = inputPath };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Determine if this is an image or video file
            if (_imageService.IsVideoFile(inputPath))
            {
                return await ProcessVideoFileAsync(inputPath, outputPath, options, progressBar, cancellationToken);
            }
            else
            {
                return await ProcessImageFileAsync(inputPath, outputPath, options, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<ProcessingResult> ProcessImageFileAsync(
        string inputPath,
        string outputPath,
        ProcessingOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = new ProcessingResult { InputPath = inputPath };

        try
        {
            // Load the image
            var image = await _imageService.LoadImageAsync(inputPath);
            if (image == null)
            {
                result.ErrorMessage = "Failed to load image";
                return result;
            }

            using (image)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Process the image
                var processedImage = await _imageService.ProcessImageAsync(
                    image,
                    options.Width,
                    options.Height,
                    options.Percent,
                    options.DeltaX,
                    options.Rigidity);

                if (processedImage == null)
                {
                    result.ErrorMessage = "Failed to process image";
                    return result;
                }

                using (processedImage)
                {
                    // Generate output path
                    var outputFileName = Path.GetFileNameWithoutExtension(inputPath) + Constants.OutputSuffix + Path.GetExtension(inputPath);
                    result.OutputPath = Path.Combine(outputPath, outputFileName);

                    // Save the processed image
                    var saved = await _imageService.SaveImageAsync(processedImage, result.OutputPath);
                    if (!saved)
                    {
                        result.ErrorMessage = "Failed to save image";
                        return result;
                    }

                    result.Success = true;
                }
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<ProcessingResult> ProcessVideoFileAsync(
        string inputPath,
        string outputPath,
        ProcessingOptions options,
        ProgressBar progressBar,
        CancellationToken cancellationToken = default)
    {
        var result = new ProcessingResult { InputPath = inputPath };

        try
        {
            // Validate input file
            if (!File.Exists(inputPath))
            {
                result.ErrorMessage = "Video file does not exist";
                return result;
            }

            var fileInfo = new FileInfo(inputPath);
            if (fileInfo.Length == 0)
            {
                result.ErrorMessage = "Video file is empty";
                return result;
            }

            Console.WriteLine($"Processing video: {Path.GetFileName(inputPath)} ({fileInfo.Length / 1024 / 1024:F1} MB)");

            // Extract frames from video
            var frames = await _videoService.ExtractFramesAsync(inputPath, cancellationToken);

            if (frames.Count == 0)
            {
                result.ErrorMessage = "Failed to extract any frames from video. Check if FFmpeg is properly installed and the video file is valid.";
                return result;
            }

            Console.WriteLine($"Successfully extracted {frames.Count} frames from video");

            // Validate extracted frames
            var validFrames = frames.Where(f => f.Data?.Length > 0 && f.Width > 0 && f.Height > 0).ToList();
            if (validFrames.Count == 0)
            {
                result.ErrorMessage = "All extracted frames are invalid (empty data or zero dimensions)";
                return result;
            }

            if (validFrames.Count < frames.Count)
            {
                Console.WriteLine($"Warning: {frames.Count - validFrames.Count} frames were invalid and will be skipped");
            }

            // Create output subfolder for video frames
            var videoName = Path.GetFileNameWithoutExtension(inputPath);
            var videoOutputPath = Path.Combine(outputPath, $"{videoName}{Constants.OutputSuffix}");

            try
            {
                Directory.CreateDirectory(videoOutputPath);
            }
            catch (Exception dirEx)
            {
                result.ErrorMessage = $"Failed to create output directory: {dirEx.Message}";
                return result;
            }

            // Update the main progress bar for frame processing
            progressBar.MaxTicks = validFrames.Count;
            progressBar.Message = "Processing frames";

            // Process frames in parallel
            var frameResults = await ProcessVideoFramesAsync(
                validFrames,
                videoOutputPath,
                videoName,
                options,
                progressBar,
                cancellationToken);

            result.OutputPath = videoOutputPath;
            var successCount = frameResults.Count(r => r.Success);
            var failureCount = frameResults.Count(r => !r.Success);

            result.Success = successCount > 0;

            if (!result.Success)
            {
                result.ErrorMessage = "Failed to process any video frames successfully";
            }
            else if (failureCount > 0)
            {
                Console.WriteLine($"Video processing completed with {successCount} successful and {failureCount} failed frames");
            }
            else
            {
                Console.WriteLine($"Video processing completed successfully with all {successCount} frames processed");
            }
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Video processing was cancelled";
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Video processing failed: {ex.Message}";
        }

        return result;
    }

    private async Task<List<ProcessingResult>> ProcessVideoFramesAsync(
        List<VideoFrame> frames,
        string outputPath,
        string videoName,
        ProcessingOptions options,
        ProgressBar frameProgressBar,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProcessingResult>();

        // Timing and progress tracking
        var startTime = DateTime.Now;
        var completedCount = new SharedCounter();

        // Create a channel for frame processing
        var channel = Channel.CreateUnbounded<VideoFrame>();

        // Producer task - adds all frames to the channel
        var producer = Task.Run(async () =>
        {
            foreach (var frame in frames)
            {
                await channel.Writer.WriteAsync(frame, cancellationToken);
            }
            channel.Writer.Complete();
        }, cancellationToken);

        // Consumer tasks - process frames from the channel
        var semaphore = new SemaphoreSlim(_config.MaxVideoThreads, _config.MaxVideoThreads);
        var processingTasks = new List<Task<ProcessingResult>>();

        await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () => await ProcessSingleVideoFrameAsync(
                frame,
                outputPath,
                videoName,
                options,
                frameProgressBar,
                semaphore,
                startTime,
                frames.Count,
                completedCount,
                cancellationToken), cancellationToken);

            processingTasks.Add(task);
        }

        // Wait for all processing to complete
        var allResults = await Task.WhenAll(processingTasks);
        results.AddRange(allResults);

        return results;
    }

    private async Task<ProcessingResult> ProcessSingleVideoFrameAsync(
        VideoFrame frame,
        string outputPath,
        string videoName,
        ProcessingOptions options,
        ProgressBar frameProgressBar,
        SemaphoreSlim semaphore,
        DateTime startTime,
        int totalFrames,
        SharedCounter completedCount,
        CancellationToken cancellationToken = default)
    {
        var result = new ProcessingResult { InputPath = $"{videoName}-frame-{frame.FrameIndex}" };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Validate frame
            var validationError = ValidateFrame(frame);
            if (validationError != null)
            {
                result.ErrorMessage = validationError;
                UpdateFrameProgress(frame, result, frameProgressBar, completedCount, totalFrames, startTime);
                return result;
            }

            // Convert and process frame
            var processedImage = await ConvertAndProcessFrame(frame, options);
            if (processedImage == null)
            {
                result.ErrorMessage = "Failed to convert or process frame";
                UpdateFrameProgress(frame, result, frameProgressBar, completedCount, totalFrames, startTime);
                return result;
            }

            using (processedImage)
            {
                // Save and verify frame
                result.OutputPath = GenerateFrameOutputPath(outputPath, videoName, frame.FrameIndex);
                var saveError = await SaveAndVerifyFrame(processedImage, result.OutputPath);
                if (saveError != null)
                {
                    result.ErrorMessage = saveError;
                    UpdateFrameProgress(frame, result, frameProgressBar, completedCount, totalFrames, startTime);
                    return result;
                }

                result.Success = true;
            }

            // Update progress
            UpdateFrameProgress(frame, result, frameProgressBar, completedCount, totalFrames, startTime);
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Frame processing was cancelled";
            completedCount.Increment();
            frameProgressBar.Tick($"Cancelled: frame-{frame.FrameIndex:D4}");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Frame processing failed: {ex.Message}";
            completedCount.Increment();
            frameProgressBar.Tick($"Failed: frame-{frame.FrameIndex:D4}");
        }
        finally
        {
            semaphore.Release();
        }

        return result;
    }

    /// <summary>
    /// Validates a video frame's data and dimensions.
    /// </summary>
    private string? ValidateFrame(VideoFrame frame)
    {
        if (frame.Data == null || frame.Data.Length == 0)
        {
            return "Frame has no data";
        }

        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return $"Frame has invalid dimensions: {frame.Width}x{frame.Height}";
        }

        return null;
    }

    /// <summary>
    /// Converts a video frame to MagickImage and processes it.
    /// </summary>
    private async Task<MagickImage?> ConvertAndProcessFrame(VideoFrame frame, ProcessingOptions options)
    {
        // Convert video frame to MagickImage
        var image = await _videoService.ConvertFrameToMagickImageAsync(frame);
        if (image == null)
        {
            return null;
        }

        using (image)
        {
            // Validate converted image
            if (image.Width == 0 || image.Height == 0)
            {
                return null;
            }

            // Process the frame
            var processedImage = await _imageService.ProcessImageAsync(
                image,
                options.Width,
                options.Height,
                options.Percent,
                options.DeltaX,
                options.Rigidity);

            if (processedImage == null)
            {
                return null;
            }

            // Validate processed image
            if (processedImage.Width == 0 || processedImage.Height == 0)
            {
                processedImage.Dispose();
                return null;
            }

            return processedImage;
        }
    }

    /// <summary>
    /// Saves a processed frame and verifies the saved file.
    /// </summary>
    private async Task<string?> SaveAndVerifyFrame(MagickImage processedImage, string outputPath)
    {
        var saved = await _imageService.SaveImageAsync(processedImage, outputPath);
        if (!saved)
        {
            return $"Failed to save processed frame to {outputPath}";
        }

        // Verify the saved file exists and has content
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            return $"Saved frame file is missing or empty: {outputPath}";
        }

        return null;
    }

    /// <summary>
    /// Generates the output file path for a video frame.
    /// </summary>
    private string GenerateFrameOutputPath(string outputFolder, string videoName, int frameIndex)
    {
        var frameFileName = $"{videoName}-frame-{frameIndex:D4}{Constants.OutputSuffix}.jpg";
        return Path.Combine(outputFolder, frameFileName);
    }

    /// <summary>
    /// Updates progress for a completed video frame.
    /// </summary>
    private void UpdateFrameProgress(
        VideoFrame frame,
        ProcessingResult result,
        ProgressBar frameProgressBar,
        SharedCounter completedCount,
        int totalFrames,
        DateTime startTime)
    {
        var currentCompleted = completedCount.Increment();
        _progressTracker.UpdateProgress(
            currentCompleted,
            totalFrames,
            startTime,
            frameProgressBar,
            $"frame-{frame.FrameIndex:D4}",
            result.Success);
    }
}
