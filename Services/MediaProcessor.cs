using System.Threading.Channels;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using nathanbutlerDEV.cascaler.Infrastructure;
using nathanbutlerDEV.cascaler.Infrastructure.Options;
using nathanbutlerDEV.cascaler.Models;
using nathanbutlerDEV.cascaler.Services.Interfaces;
using nathanbutlerDEV.cascaler.Utilities;
using ShellProgressBar;

namespace nathanbutlerDEV.cascaler.Services;

/// <summary>
/// Orchestrates batch processing of media files with parallel execution and progress tracking.
/// </summary>
public class MediaProcessor : IMediaProcessor
{
    private readonly IImageProcessingService _imageService;
    private readonly IVideoProcessingService _videoService;
    private readonly IVideoCompilationService _videoCompilationService;
    private readonly IProgressTracker _progressTracker;
    private readonly IDimensionInterpolator _dimensionInterpolator;
    private readonly ProcessingSettings _processingSettings;
    private readonly OutputOptions _outputOptions;
    private readonly ILogger<MediaProcessor> _logger;

    public MediaProcessor(
        IImageProcessingService imageService,
        IVideoProcessingService videoService,
        IVideoCompilationService videoCompilationService,
        IProgressTracker progressTracker,
        IDimensionInterpolator dimensionInterpolator,
        IOptions<ProcessingSettings> processingSettings,
        IOptions<OutputOptions> outputOptions,
        ILogger<MediaProcessor> logger)
    {
        _imageService = imageService;
        _videoService = videoService;
        _videoCompilationService = videoCompilationService;
        _progressTracker = progressTracker;
        _dimensionInterpolator = dimensionInterpolator;
        _processingSettings = processingSettings.Value;
        _outputOptions = outputOptions.Value;
        _logger = logger;
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

        ProgressBar? progressBar = null;

        if (options.ShowProgress)
        {
            var progressOptions = new ProgressBarOptions
            {
                ProgressCharacter = _outputOptions.ProgressCharacter[0],
                ProgressBarOnBottom = true,
                ShowEstimatedDuration = _outputOptions.ShowEstimatedDuration,
                DisableBottomPercentage = false
            };

            // Hide cursor during progress bar operation
            Console.CursorVisible = false;

            progressBar = new ProgressBar(inputFiles.Count, "Processing images", progressOptions);
        }
        else
        {
            _logger.LogInformation("Processing {FileCount} media file(s) with {ThreadCount} thread(s)", inputFiles.Count, options.MaxThreads);
        }

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

        // Dispose progress bar if it was created
        progressBar?.Dispose();

        // Show info messages after progress bar completes
        Console.WriteLine();
        foreach (var result in results.Where(r => r.InfoMessages.Any()))
        {
            foreach (var msg in result.InfoMessages)
            {
                Console.WriteLine(msg);
            }
            Console.WriteLine();
        }

        return results;
    }

    private async Task<ProcessingResult> ProcessSingleMediaWrapperAsync(
        string inputPath,
        string outputPath,
        ProcessingOptions options,
        ProgressBar? progressBar,
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
        ProgressBar? progressBar,
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
                return await ProcessImageFileAsync(inputPath, outputPath, options, progressBar, cancellationToken);
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
        ProgressBar? progressBar,
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

                // Check if this is image-to-sequence conversion
                if (options.IsImageSequence)
                {
                    // For image sequences, create new timing and counter for frame progress
                    var sequenceStartTime = DateTime.Now;
                    var sequenceCounter = new SharedCounter();

                    // Generate a sequence of frames
                    return await ProcessImageToSequenceAsync(
                        image,
                        outputPath,
                        options,
                        progressBar,
                        sequenceStartTime,
                        sequenceCounter,
                        cancellationToken);
                }

                // Process the image (single output)
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
                    // Generate output path based on processing mode
                    if (options.Mode == ProcessingMode.SingleImage)
                    {
                        // For single image, outputPath is already the full target file path
                        result.OutputPath = outputPath;
                    }
                    else
                    {
                        // For batch processing, preserve original filename without -cas suffix
                        var outputFileName = Path.GetFileName(inputPath);
                        result.OutputPath = Path.Combine(outputPath, outputFileName);
                    }

                    // Determine output format
                    var outputFormat = options.Format;

                    // Save the processed image
                    var saved = await _imageService.SaveImageAsync(processedImage, result.OutputPath, outputFormat);
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

    /// <summary>
    /// Processes a single image into a sequence of frames with optional gradual scaling.
    /// </summary>
    private async Task<ProcessingResult> ProcessImageToSequenceAsync(
        MagickImage sourceImage,
        string outputPath,
        ProcessingOptions options,
        ProgressBar? progressBar,
        DateTime startTime,
        SharedCounter completedCount,
        CancellationToken cancellationToken = default)
    {
        var result = new ProcessingResult { InputPath = "Image sequence generation" };

        try
        {
            if (!options.Duration.HasValue)
            {
                result.ErrorMessage = "Duration must be specified for image-to-sequence conversion";
                return result;
            }

            // Calculate total frames
            int totalFrames = (int)Math.Round(options.Duration.Value * options.Fps);

            if (totalFrames <= 0)
            {
                result.ErrorMessage = "Duration and FPS must result in at least 1 frame";
                return result;
            }

            // Store original dimensions for scale-back
            int originalWidth = (int)sourceImage.Width;
            int originalHeight = (int)sourceImage.Height;

            // Get start and end dimensions
            var (startWidth, startHeight) = _dimensionInterpolator.GetStartDimensions(
                originalWidth,
                originalHeight,
                options);

            var (endWidth, endHeight) = _dimensionInterpolator.GetEndDimensions(
                originalWidth,
                originalHeight,
                options);

            _logger.LogInformation("Generating {TotalFrames} frames from {StartWidth}x{StartHeight} to {EndWidth}x{EndHeight}", totalFrames, startWidth, startHeight, endWidth, endHeight);
            if (options.IsGradualScaling)
            {
                _logger.LogInformation("Frames will be scaled back to original dimensions: {OriginalWidth}x{OriginalHeight}", originalWidth, originalHeight);
            }

            // Update progress bar for frame processing
            if (progressBar != null)
            {
                progressBar.MaxTicks = totalFrames;
                progressBar.Message = "Generating frames";
            }
            else
            {
                _logger.LogInformation("Generating {TotalFrames} frames", totalFrames);
            }

            // Check if video output is requested
            if (options.IsVideoOutput)
            {
                return await ProcessImageToVideoAsync(
                    sourceImage,
                    outputPath,
                    options,
                    totalFrames,
                    originalWidth,
                    originalHeight,
                    startWidth,
                    startHeight,
                    endWidth,
                    endHeight,
                    progressBar,
                    startTime,
                    completedCount,
                    cancellationToken);
            }

            // Otherwise, save frames as image files
            // Create output directory
            Directory.CreateDirectory(outputPath);

            // Determine output format (default to PNG for image sequences)
            var outputFormat = options.Format ?? _processingSettings.DefaultVideoFrameFormat;

            int successCount = 0;

            // Generate each frame
            for (int i = 0; i < totalFrames; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Calculate dimensions for this frame
                int frameWidth, frameHeight;
                if (options.IsGradualScaling && totalFrames > 1)
                {
                    double t = (double)i / (totalFrames - 1);
                    frameWidth = (int)Math.Round(startWidth + (endWidth - startWidth) * t);
                    frameHeight = (int)Math.Round(startHeight + (endHeight - startHeight) * t);
                }
                else
                {
                    // No gradual scaling, use end dimensions
                    frameWidth = endWidth;
                    frameHeight = endHeight;
                }

                // Process image to this frame's dimensions
                var processedImage = await _imageService.ProcessImageAsync(
                    sourceImage,
                    frameWidth,
                    frameHeight,
                    null, // Don't use percent, use absolute dimensions
                    options.DeltaX,
                    options.Rigidity);

                if (processedImage == null)
                {
                    _logger.LogWarning("Failed to process frame {FrameNumber}", i + 1);
                    // Update progress even for failed frames
                    var currentCompleted = completedCount.Increment();
                    _progressTracker.UpdateProgress(
                        currentCompleted,
                        totalFrames,
                        startTime,
                        progressBar,
                        $"frame-{i:D4}",
                        false);
                    continue;
                }

                using (processedImage)
                {
                    // If gradual scaling is enabled, scale back to original dimensions
                    if (options.IsGradualScaling)
                    {
                        var geometry = new MagickGeometry((uint)originalWidth, (uint)originalHeight)
                        {
                            IgnoreAspectRatio = true
                        };
                        processedImage.Resize(geometry);
                    }

                    // Generate frame filename
                    var frameFileName = GenerateFrameOutputPath(outputPath, "frame", i, outputFormat);

                    // Save the frame
                    var saved = await _imageService.SaveImageAsync(processedImage, frameFileName, outputFormat);
                    if (saved)
                    {
                        successCount++;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to save frame {FrameNumber}", i + 1);
                    }

                    // Update progress
                    var currentCompleted = completedCount.Increment();
                    _progressTracker.UpdateProgress(
                        currentCompleted,
                        totalFrames,
                        startTime,
                        progressBar,
                        $"frame-{i:D4}",
                        saved);
                }
            }

            result.OutputPath = outputPath;
            result.Success = successCount > 0;

            if (!result.Success)
            {
                result.ErrorMessage = "Failed to generate any frames";
            }
            else if (successCount < totalFrames)
            {
                _logger.LogInformation("Image sequence generation completed with {SuccessCount}/{TotalFrames} frames", successCount, totalFrames);
            }
            else
            {
                _logger.LogInformation("Image sequence generation completed successfully with {SuccessCount} frames", successCount);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Image sequence generation failed: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Processes a single image into a video file using streaming encoding.
    /// </summary>
    private async Task<ProcessingResult> ProcessImageToVideoAsync(
        MagickImage sourceImage,
        string outputVideoPath,
        ProcessingOptions options,
        int totalFrames,
        int originalWidth,
        int originalHeight,
        int startWidth,
        int startHeight,
        int endWidth,
        int endHeight,
        ProgressBar? progressBar,
        DateTime startTime,
        SharedCounter completedCount,
        CancellationToken cancellationToken = default)
    {
        var result = new ProcessingResult { InputPath = "Image to video" };

        try
        {
            _logger.LogInformation("Starting streaming video encoder for {TotalFrames} frames at {Fps} fps", totalFrames, options.Fps);

            // Start the streaming encoder
            var (submitFrame, encodingComplete) = await _videoCompilationService.StartStreamingEncoderAsync(
                outputVideoPath,
                originalWidth,
                originalHeight,
                options.Fps,
                totalFrames,
                cancellationToken);

            // Process and stream frames
            for (int i = 0; i < totalFrames; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Calculate dimensions for this frame
                int frameWidth, frameHeight;
                if (options.IsGradualScaling && totalFrames > 1)
                {
                    double t = (double)i / (totalFrames - 1);
                    frameWidth = (int)Math.Round(startWidth + (endWidth - startWidth) * t);
                    frameHeight = (int)Math.Round(startHeight + (endHeight - startHeight) * t);
                }
                else
                {
                    frameWidth = endWidth;
                    frameHeight = endHeight;
                }

                // Process image to this frame's dimensions
                var processedImage = await _imageService.ProcessImageAsync(
                    sourceImage,
                    frameWidth,
                    frameHeight,
                    null,
                    options.DeltaX,
                    options.Rigidity);

                if (processedImage == null)
                {
                    _logger.LogWarning("Failed to process frame {FrameNumber}", i + 1);
                    _progressTracker.UpdateProgress(
                        completedCount.Increment(),
                        totalFrames,
                        startTime,
                        progressBar,
                        $"frame-{i:D4}",
                        false);
                    continue;
                }

                // Scale back to original dimensions if gradual scaling
                if (options.IsGradualScaling)
                {
                    var geometry = new MagickGeometry((uint)originalWidth, (uint)originalHeight)
                    {
                        IgnoreAspectRatio = true
                    };
                    processedImage.Resize(geometry);
                }

                // Submit frame to encoder (ownership transferred)
                await submitFrame(i, processedImage);

                // Update progress
                _progressTracker.UpdateProgress(
                    completedCount.Increment(),
                    totalFrames,
                    startTime,
                    progressBar,
                    $"frame-{i:D4}",
                    true);
            }

            // Wait for encoding to complete
            _logger.LogInformation("Waiting for video encoding to complete");
            await encodingComplete;

            result.OutputPath = outputVideoPath;
            result.Success = true;
            _logger.LogInformation("Video generation completed: {OutputVideoPath}", outputVideoPath);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Video generation failed: {ex.Message}";
            _logger.LogError(ex, "Video generation failed");
        }

        return result;
    }

    /// <summary>
    /// Processes a video file into another video file using streaming encoding with audio preservation.
    /// </summary>
    private async Task<ProcessingResult> ProcessVideoToVideoAsync(
        string inputVideoPath,
        List<VideoFrame> frames,
        string outputVideoPath,
        ProcessingOptions options,
        double videoFps,
        int originalWidth,
        int originalHeight,
        ProgressBar? progressBar,
        CancellationToken cancellationToken = default)
    {
        var result = new ProcessingResult { InputPath = inputVideoPath };
        string? tempAudioPath = null;
        string? tempVideoPath = null;

        _logger.LogDebug("ProcessVideoToVideoAsync called - inputVideoPath: {InputVideoPath}, outputVideoPath: {OutputVideoPath}", inputVideoPath, outputVideoPath);

        try
        {
            // Extract audio from source video
            var tempDir = Path.Combine(Path.GetTempPath(), $"{Constants.TempFramesFolderPrefix}{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            tempAudioPath = Path.Combine(tempDir, "audio_temp.m4a");

            _logger.LogDebug("Temp directory created - tempDir: {TempDir}, tempAudioPath: {TempAudioPath}", tempDir, tempAudioPath);

            result.InfoMessages.Add("=== Audio Processing ===");
            // Calculate audio trim parameters (convert frame indices to time)
            double? audioStart = null;
            double? audioDuration = null;

            if (options.Start.HasValue)
            {
                audioStart = options.Start.Value;
            }

            if (options.Duration.HasValue)
            {
                audioDuration = options.Duration.Value;
            }
            else if (options.End.HasValue && options.Start.HasValue)
            {
                audioDuration = options.End.Value - options.Start.Value;
            }

            if (audioStart.HasValue || audioDuration.HasValue)
            {
                var trimInfo = audioStart.HasValue
                    ? $"from {audioStart.Value:F3}s"
                    : "from start";
                if (audioDuration.HasValue)
                {
                    trimInfo += $" for {audioDuration.Value:F3}s";
                }
                result.InfoMessages.Add($"  Audio trimming: {trimInfo}");
            }

            // Calculate dimension information for gradual scaling
            int startWidth, startHeight, endWidth, endHeight;

            if (options.IsGradualScaling)
            {
                var (sw, sh) = _dimensionInterpolator.GetStartDimensions(originalWidth, originalHeight, options);
                var (ew, eh) = _dimensionInterpolator.GetEndDimensions(originalWidth, originalHeight, options);
                startWidth = sw;
                startHeight = sh;
                endWidth = ew;
                endHeight = eh;

                _logger.LogInformation("Gradual scaling enabled: {StartWidth}x{StartHeight} → {EndWidth}x{EndHeight}", startWidth, startHeight, endWidth, endHeight);
                _logger.LogInformation("Frames will be scaled back to original dimensions: {OriginalWidth}x{OriginalHeight}", originalWidth, originalHeight);
            }
            else
            {
                startWidth = originalWidth;
                startHeight = originalHeight;
                endWidth = options.Width ?? (int)(originalWidth * (options.Percent ?? 50) / 100.0);
                endHeight = options.Height ?? (int)(originalHeight * (options.Percent ?? 50) / 100.0);
            }

            // Start the unified streaming encoder (handles both video and audio in single pass)
            _logger.LogInformation("Starting unified video encoder for {FrameCount} frames at {VideoFps} fps", frames.Count, videoFps);
            result.InfoMessages.Add("Encoding video with audio (single-pass)...");

            var (submitFrame, encodingComplete) = await _videoCompilationService.StartStreamingEncoderWithAudioAsync(
                inputVideoPath,  // Source video for audio extraction
                outputVideoPath,  // Final output path
                originalWidth,
                originalHeight,
                videoFps,
                frames.Count,
                options,  // Processing options including video encoding settings
                audioStart,
                audioDuration,
                cancellationToken);

            // Update progress bar
            if (progressBar != null)
            {
                progressBar.MaxTicks = frames.Count;
                progressBar.Message = "Processing frames";
            }
            else
            {
                _logger.LogInformation("Processing {FrameCount} frames", frames.Count);
            }

            // Process frames in parallel using channel
            var frameChannel = Channel.CreateUnbounded<(VideoFrame frame, int index)>();
            var completedCount = new SharedCounter();
            var startTime = DateTime.Now;

            // Producer: add all frames to channel
            var producer = Task.Run(async () =>
            {
                for (int i = 0; i < frames.Count; i++)
                {
                    await frameChannel.Writer.WriteAsync((frames[i], i), cancellationToken);
                }
                frameChannel.Writer.Complete();
            }, cancellationToken);

            // Consumers: process frames in parallel
            var semaphore = new SemaphoreSlim(_processingSettings.MaxVideoThreads, _processingSettings.MaxVideoThreads);
            var processingTasks = new List<Task>();

            await foreach (var (frame, frameIndex) in frameChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await semaphore.WaitAsync(cancellationToken);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        // Calculate dimensions for this frame
                        int frameWidth, frameHeight;
                        if (options.IsGradualScaling && frames.Count > 1)
                        {
                            double t = (double)frameIndex / (frames.Count - 1);
                            frameWidth = (int)Math.Round(startWidth + (endWidth - startWidth) * t);
                            frameHeight = (int)Math.Round(startHeight + (endHeight - startHeight) * t);
                        }
                        else
                        {
                            frameWidth = endWidth;
                            frameHeight = endHeight;
                        }

                        // Convert and process frame
                        var magickImage = await _videoService.ConvertFrameToMagickImageAsync(frame);
                        if (magickImage == null)
                        {
                            _logger.LogWarning("Failed to convert frame {FrameIndex}", frameIndex);
                            return;
                        }

                        using (magickImage)
                        {
                            var processedImage = await _imageService.ProcessImageAsync(
                                magickImage,
                                frameWidth,
                                frameHeight,
                                null,
                                options.DeltaX,
                                options.Rigidity);

                            if (processedImage == null)
                            {
                                _logger.LogWarning("Failed to process frame {FrameIndex}", frameIndex);
                                return;
                            }

                            // Scale back to original dimensions if gradual scaling
                            if (options.IsGradualScaling)
                            {
                                var geometry = new MagickGeometry((uint)originalWidth, (uint)originalHeight)
                                {
                                    IgnoreAspectRatio = true
                                };
                                processedImage.Resize(geometry);
                            }

                            // Submit frame to encoder (ownership transferred)
                            await submitFrame(frameIndex, processedImage);
                        }

                        // Update progress
                        var completed = completedCount.Increment();
                        _progressTracker.UpdateProgress(
                            completed,
                            frames.Count,
                            startTime,
                            progressBar,
                            $"frame-{frameIndex:D4}",
                            true);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                processingTasks.Add(task);
            }

            // Wait for all processing to complete
            await Task.WhenAll(processingTasks);

            // Wait for encoding to complete (audio is already merged in single pass)
            await encodingComplete;

            result.InfoMessages.Add("✓ Video encoding completed with audio");


            result.OutputPath = outputVideoPath;
            result.Success = File.Exists(outputVideoPath);

            if (result.Success)
            {
                _logger.LogInformation("Video processing completed: {OutputVideoPath}", outputVideoPath);
            }
            else
            {
                result.ErrorMessage = "Video encoding completed but output file not found";
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Video processing failed: {ex.Message}";
            _logger.LogError(ex, "Video processing failed");
        }
        finally
        {
            // Cleanup temp files
            try
            {
                if (tempAudioPath != null && File.Exists(tempAudioPath))
                    File.Delete(tempAudioPath);
                if (tempVideoPath != null && File.Exists(tempVideoPath))
                    File.Delete(tempVideoPath);

                if (tempAudioPath != null)
                {
                    var tempDir = Path.GetDirectoryName(tempAudioPath);
                    if (tempDir != null && Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        return result;
    }

    private async Task<ProcessingResult> ProcessVideoFileAsync(
        string inputPath,
        string outputPath,
        ProcessingOptions options,
        ProgressBar? progressBar,
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

            _logger.LogInformation("Processing video: {VideoFile} ({SizeMB:F1} MB)", Path.GetFileName(inputPath), fileInfo.Length / 1024.0 / 1024.0);

            // Calculate frame range if trimming is requested
            int? startFrame = null;
            int? endFrame = null;

            if (options.HasVideoTrim)
            {
                var frameRange = await _videoService.CalculateFrameRangeAsync(
                    inputPath,
                    options.Start,
                    options.End,
                    options.Duration);

                if (frameRange.HasValue)
                {
                    startFrame = frameRange.Value.startFrame;
                    endFrame = frameRange.Value.endFrame;
                    _logger.LogInformation("Trimming video to frames {StartFrame}-{EndFrame}", startFrame, endFrame);
                }
                else
                {
                    result.ErrorMessage = "Failed to calculate frame range for video trimming";
                    return result;
                }
            }

            // Extract frames from video (with optional trimming)
            var frames = await _videoService.ExtractFramesAsync(inputPath, startFrame, endFrame, cancellationToken);

            if (frames.Count == 0)
            {
                result.ErrorMessage = "Failed to extract any frames from video. Check if FFmpeg is properly installed and the video file is valid.";
                return result;
            }

            _logger.LogInformation("Successfully extracted {FrameCount} frames from video", frames.Count);

            // Validate extracted frames
            var validFrames = frames.Where(f => f.Data?.Length > 0 && f.Width > 0 && f.Height > 0).ToList();
            if (validFrames.Count == 0)
            {
                result.ErrorMessage = "All extracted frames are invalid (empty data or zero dimensions)";
                return result;
            }

            if (validFrames.Count < frames.Count)
            {
                _logger.LogWarning("{InvalidCount} frames were invalid and will be skipped", frames.Count - validFrames.Count);
            }

            // Get original dimensions for gradual scaling calculations
            int originalWidth = validFrames.Count > 0 ? validFrames[0].Width : 0;
            int originalHeight = validFrames.Count > 0 ? validFrames[0].Height : 0;

            // Get video FPS
            var videoInfo = await _videoService.GetVideoInfoAsync(inputPath);
            double videoFps = videoInfo?.frameRate ?? 25.0;

            // Check if video output is requested
            if (options.IsVideoOutput)
            {
                _logger.LogDebug("Video output detected, calling ProcessVideoToVideoAsync - Output path: {OutputPath}", outputPath);
                var videoResult = await ProcessVideoToVideoAsync(
                    inputPath,
                    validFrames,
                    outputPath,
                    options,
                    videoFps,
                    originalWidth,
                    originalHeight,
                    progressBar,
                    cancellationToken);
                _logger.LogDebug("ProcessVideoToVideoAsync completed, InfoMessages count: {InfoMessageCount}", videoResult.InfoMessages.Count);
                return videoResult;
            }

            // Otherwise, save frames as image files
            var videoOutputPath = outputPath;
            var videoName = Path.GetFileNameWithoutExtension(inputPath);

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
            if (progressBar != null)
            {
                progressBar.MaxTicks = validFrames.Count;
                progressBar.Message = "Processing frames";
            }
            else
            {
                _logger.LogInformation("Processing {ValidFrameCount} frames", validFrames.Count);
            }

            // Process frames in parallel
            var frameResults = await ProcessVideoFramesAsync(
                validFrames,
                videoOutputPath,
                videoName,
                options,
                originalWidth,
                originalHeight,
                progressBar!,
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
                _logger.LogInformation("Video processing completed with {SuccessCount} successful and {FailureCount} failed frames", successCount, failureCount);
            }
            else
            {
                _logger.LogInformation("Video processing completed successfully with all {SuccessCount} frames processed", successCount);
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
        int? originalWidth,
        int? originalHeight,
        ProgressBar frameProgressBar,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProcessingResult>();

        // Timing and progress tracking
        var startTime = DateTime.Now;
        var completedCount = new SharedCounter();

        // Calculate dimension information for gradual scaling
        int? startWidth = null, startHeight = null, endWidth = null, endHeight = null;

        if (options.IsGradualScaling && originalWidth.HasValue && originalHeight.HasValue)
        {
            var (sw, sh) = _dimensionInterpolator.GetStartDimensions(
                originalWidth.Value,
                originalHeight.Value,
                options);
            var (ew, eh) = _dimensionInterpolator.GetEndDimensions(
                originalWidth.Value,
                originalHeight.Value,
                options);

            startWidth = sw;
            startHeight = sh;
            endWidth = ew;
            endHeight = eh;

            _logger.LogInformation("Gradual scaling enabled: {StartWidth}x{StartHeight} → {EndWidth}x{EndHeight}", startWidth, startHeight, endWidth, endHeight);
            if (originalWidth.HasValue && originalHeight.HasValue)
            {
                _logger.LogInformation("Frames will be scaled back to original dimensions: {OriginalWidth}x{OriginalHeight}", originalWidth, originalHeight);
            }
        }

        // Create a channel for frame processing
        var channel = Channel.CreateUnbounded<(VideoFrame frame, int frameNumber)>();

        // Producer task - adds all frames to the channel with their sequence number
        var producer = Task.Run(async () =>
        {
            for (int i = 0; i < frames.Count; i++)
            {
                await channel.Writer.WriteAsync((frames[i], i), cancellationToken);
            }
            channel.Writer.Complete();
        }, cancellationToken);

        // Consumer tasks - process frames from the channel
        var semaphore = new SemaphoreSlim(_processingSettings.MaxVideoThreads, _processingSettings.MaxVideoThreads);
        var processingTasks = new List<Task<ProcessingResult>>();

        await foreach (var (frame, frameNumber) in channel.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () => await ProcessSingleVideoFrameAsync(
                frame,
                frameNumber,
                frames.Count,
                outputPath,
                videoName,
                options,
                startWidth,
                startHeight,
                endWidth,
                endHeight,
                originalWidth,
                originalHeight,
                frameProgressBar,
                semaphore,
                startTime,
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
        int frameNumber,
        int totalFrames,
        string outputPath,
        string videoName,
        ProcessingOptions options,
        int? startWidth,
        int? startHeight,
        int? endWidth,
        int? endHeight,
        int? originalWidth,
        int? originalHeight,
        ProgressBar frameProgressBar,
        SemaphoreSlim semaphore,
        DateTime startTime,
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

            // Calculate dimensions for this frame if gradual scaling is enabled
            int? frameWidth = null;
            int? frameHeight = null;
            bool isGradualScaling = false;

            if (startWidth.HasValue && startHeight.HasValue && endWidth.HasValue && endHeight.HasValue && totalFrames > 1)
            {
                isGradualScaling = true;
                double t = (double)frameNumber / (totalFrames - 1);
                frameWidth = (int)Math.Round(startWidth.Value + (endWidth.Value - startWidth.Value) * t);
                frameHeight = (int)Math.Round(startHeight.Value + (endHeight.Value - startHeight.Value) * t);
            }

            // Convert and process frame
            var processedImage = await ConvertAndProcessFrame(frame, options, frameWidth, frameHeight);
            if (processedImage == null)
            {
                result.ErrorMessage = "Failed to convert or process frame";
                UpdateFrameProgress(frame, result, frameProgressBar, completedCount, totalFrames, startTime);
                return result;
            }

            using (processedImage)
            {
                // If gradual scaling is enabled, scale back to original dimensions
                if (isGradualScaling && originalWidth.HasValue && originalHeight.HasValue)
                {
                    var geometry = new MagickGeometry((uint)originalWidth.Value, (uint)originalHeight.Value)
                    {
                        IgnoreAspectRatio = true
                    };
                    processedImage.Resize(geometry);
                }

                // Determine output format (default to PNG for video frames)
                var outputFormat = options.Format ?? _processingSettings.DefaultVideoFrameFormat;

                // Save and verify frame
                result.OutputPath = GenerateFrameOutputPath(outputPath, "frame", frameNumber, outputFormat);
                var saveError = await SaveAndVerifyFrame(processedImage, result.OutputPath, outputFormat);
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
    private async Task<MagickImage?> ConvertAndProcessFrame(
        VideoFrame frame,
        ProcessingOptions options,
        int? overrideWidth = null,
        int? overrideHeight = null)
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

            // Use override dimensions if provided (for gradual scaling)
            var targetWidth = overrideWidth ?? options.Width;
            var targetHeight = overrideHeight ?? options.Height;

            // Process the frame
            var processedImage = await _imageService.ProcessImageAsync(
                image,
                targetWidth,
                targetHeight,
                overrideWidth.HasValue ? null : options.Percent, // Don't use percent if absolute dimensions provided
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
    private async Task<string?> SaveAndVerifyFrame(MagickImage processedImage, string outputPath, string? format = null)
    {
        var saved = await _imageService.SaveImageAsync(processedImage, outputPath, format);
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
    private string GenerateFrameOutputPath(string outputFolder, string baseName, int frameIndex, string? format = null)
    {
        // Determine extension from format
        var extension = ".jpg"; // default
        if (!string.IsNullOrEmpty(format) && Constants.FormatExtensions.TryGetValue(format, out var formatExt))
        {
            extension = formatExt;
        }

        // Simple frame naming: frame-0001.jpg, frame-0002.jpg, etc.
        var frameFileName = $"{baseName}-{frameIndex:D4}{extension}";
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
