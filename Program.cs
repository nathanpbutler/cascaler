using System.CommandLine;
using System.Threading.Channels;
using ImageMagick;
using ShellProgressBar;
using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;

namespace cascaler;

// Supporting classes used throughout the application
public class SharedCounter
{
    private int _value;
    
    public int Increment() => Interlocked.Increment(ref _value);
    public int Value => _value;
}

public class ProcessingResult
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

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

// Main program entry point and orchestration
internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var rootCommand = new RootCommand("cascaler - A high-performance batch liquid rescaling tool for images.");

            // Define options
            var widthOption = new Option<int?>("--width")
            {
                Description = "Width of the output image",
                Aliases = { "-w" }
            };

            var heightOption = new Option<int?>("--height")
            {
                Description = "Height of the output image",
                Aliases = { "-h" }
            };

            var percentOption = new Option<int>("--percent")
            {
                Description = "Percent of the output image (default 50)",
                Aliases = { "-p" },
                DefaultValueFactory = _ => 50
            };

            var deltaXOption = new Option<double>("--deltaX")
            {
                Description = "Maximum seam transversal step (0 means straight seams - 1 means curved seams)",
                Aliases = { "-d" },
                DefaultValueFactory = _ => 1.0
            };

            var rigidityOption = new Option<double>("--rigidity")
            {
                Description = "Introduce a bias for non-straight seams (typically 0)",
                Aliases = { "-r" },
                DefaultValueFactory = _ => 1.0
            };

            var threadsOption = new Option<int>("--threads")
            {
                Description = "Process images in parallel",
                Aliases = { "-t" },
                DefaultValueFactory = _ => 16
            };

            var outputOption = new Option<string?>("--output")
            {
                Description = "Output folder (default is input + \"-cas\")",
                Aliases = { "-o" }
            };

            var inputArgument = new Argument<string>("input")
            {
                Description = "Input image file or folder path"
            };

            // Add options and argument to root command
            rootCommand.Add(widthOption);
            rootCommand.Add(heightOption);
            rootCommand.Add(percentOption);
            rootCommand.Add(deltaXOption);
            rootCommand.Add(rigidityOption);
            rootCommand.Add(threadsOption);
            rootCommand.Add(outputOption);
            rootCommand.Add(inputArgument);

            // Create a cancellation token source for handling Ctrl+C
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\n\nCancellation requested...");
            };

            rootCommand.SetAction(async parseResult =>
            {
                var width = parseResult.GetValue(widthOption);
                var height = parseResult.GetValue(heightOption);
                var percent = parseResult.GetValue(percentOption);
                var deltaX = parseResult.GetValue(deltaXOption);
                var rigidity = parseResult.GetValue(rigidityOption);
                var threads = parseResult.GetValue(threadsOption);
                var outputPath = parseResult.GetValue(outputOption);
                var inputPath = parseResult.GetValue(inputArgument);
                var cancellationToken = cts.Token;

                if (string.IsNullOrEmpty(inputPath))
                {
                    Console.WriteLine("Error: Input path is required.");
                    return;
                }

                // Validate that either width/height or percent is provided, not both
                if ((width.HasValue || height.HasValue) && percent != 50)
                {
                    Console.WriteLine("Error: Cannot specify both width/height and percent. Choose one scaling method.");
                    return;
                }

                // Set default output path if not provided
                outputPath ??= inputPath + "-cas";

                // Collect input files (both images and videos)
                var inputFiles = new List<string>();
                if (File.Exists(inputPath))
                {
                    if (ImageProcessingService.IsMediaFile(inputPath))
                    {
                        inputFiles.Add(inputPath);
                    }
                    else
                    {
                        Console.WriteLine($"Error: Input file {inputPath} is not a supported media format.");
                        return;
                    }
                }
                else if (Directory.Exists(inputPath))
                {
                    inputFiles.AddRange(Directory.GetFiles(inputPath)
                        .Where(ImageProcessingService.IsMediaFile)
                        .OrderBy(f => f));
                }
                else
                {
                    Console.WriteLine($"Error: Input path {inputPath} does not exist.");
                    return;
                }

                if (inputFiles.Count == 0)
                {
                    Console.WriteLine($"No supported media files found in {inputPath}");
                    return;
                }

                // Create output folder if it doesn't exist
                if (!Directory.Exists(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                }

                Console.WriteLine($"Processing {inputFiles.Count} media file(s) with {threads} thread(s)...");

                try
                {
                    // Process files using modern async/await with channels
                    var results = await ProcessMediaAsync(
                        inputFiles,
                        outputPath,
                        width,
                        height,
                        percent == 50 ? null : percent,
                        deltaX,
                        rigidity,
                        threads,
                        cancellationToken);

                    // Report results
                    var successCount = results.Count(r => r.Success);
                    var failCount = results.Count(r => !r.Success);

                    Console.WriteLine($"\nProcessing complete: {successCount} succeeded, {failCount} failed");

                    if (failCount > 0)
                    {
                        Console.WriteLine("\nFailed files:");
                        foreach (var failed in results.Where(r => !r.Success))
                        {
                            Console.WriteLine($"  - {Path.GetFileName(failed.InputPath)}: {failed.ErrorMessage}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("\nOperation cancelled by user.");
                }
                finally
                {
                    // Always restore cursor visibility
                    Console.CursorVisible = true;
                }
            });

            // InvokeAsync automatically handles Ctrl+C with a CancellationToken
            return await rootCommand.Parse(args).InvokeAsync(cancellationToken: cts.Token);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<List<ProcessingResult>> ProcessMediaAsync(
        List<string> inputFiles,
        string outputPath,
        int? width,
        int? height,
        int? percent,
        double deltaX,
        double rigidity,
        int maxConcurrency,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProcessingResult>();
        var progressOptions = new ProgressBarOptions
        {
            ProgressCharacter = '─',
            ProgressBarOnBottom = true,
            ShowEstimatedDuration = true,
            DisableBottomPercentage = false
        };

        // Hide cursor during progress bar operation
        Console.CursorVisible = false;

        using var progressBar = new ProgressBar(inputFiles.Count, "Processing images", progressOptions);
        
        // Set initial estimated duration to help ShellProgressBar start calculating
        progressBar.EstimatedDuration = TimeSpan.FromMinutes(5); // Initial rough estimate
        
        // Timing and progress tracking for manual duration updates
        var startTime = DateTime.Now;
        var completedCount = new SharedCounter();

        // Create a channel for work items
        var channel = Channel.CreateUnbounded<string>();

        // Producer task - adds all files to the channel
        var producer = Task.Run(async () =>
        {
            foreach (var file in inputFiles)
            {
                await channel.Writer.WriteAsync(file);
            }
            channel.Writer.Complete();
        }, cancellationToken);

        // Consumer tasks - process files from the channel
        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var processingTasks = new List<Task<ProcessingResult>>();

        await foreach (var inputFile in channel.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () => await ProcessSingleImageWrapperAsync(
                inputFile,
                outputPath,
                width,
                height,
                percent,
                deltaX,
                rigidity,
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

    private static async Task<ProcessingResult> ProcessSingleImageWrapperAsync(
        string inputPath,
        string outputFolder,
        int? width,
        int? height,
        int? percent,
        double deltaX,
        double rigidity,
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
                outputFolder,
                width,
                height,
                percent,
                deltaX,
                rigidity,
                progressBar,
                cancellationToken);
                
            // Update progress and calculate estimated duration after each completion
            var currentCompleted = completedCount.Increment();
            var elapsed = DateTime.Now - startTime;
            
            // Calculate and update estimated duration every few completions
            if (currentCompleted >= 3 && elapsed.TotalSeconds > 1) // Only estimate after we have some data
            {
                var throughput = currentCompleted / elapsed.TotalSeconds; // images per second
                var remaining = totalFiles - currentCompleted;
                if (remaining > 0 && throughput > 0)
                {
                    var estimatedTimeRemaining = TimeSpan.FromSeconds(remaining / throughput);
                    progressBar.EstimatedDuration = estimatedTimeRemaining;
                }
            }
            
            progressBar.Tick($"{(result.Success ? "Completed" : "Failed")}: {Path.GetFileName(inputPath)}");
                                                   
            return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task<ProcessingResult> ProcessSingleMediaAsync(
        string inputPath,
        string outputFolder,
        int? width,
        int? height,
        int? percent,
        double deltaX,
        double rigidity,
        ProgressBar progressBar,
        CancellationToken cancellationToken = default)
    {
        var result = new ProcessingResult { InputPath = inputPath };

        try
        {
            // Check for cancellation
            cancellationToken.ThrowIfCancellationRequested();

            // Determine if this is an image or video file
            if (ImageProcessingService.IsVideoFile(inputPath))
            {
                return await ProcessVideoFileAsync(inputPath, outputFolder, width, height, percent, deltaX, rigidity, progressBar, cancellationToken);
            }
            else
            {
                return await ProcessImageFileAsync(inputPath, outputFolder, width, height, percent, deltaX, rigidity, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static async Task<ProcessingResult> ProcessImageFileAsync(
        string inputPath,
        string outputFolder,
        int? width,
        int? height,
        int? percent,
        double deltaX,
        double rigidity,
        CancellationToken cancellationToken = default)
    {
        var result = new ProcessingResult { InputPath = inputPath };

        try
        {
            // Load the image
            var image = await ImageProcessingService.LoadImageAsync(inputPath);
            if (image == null)
            {
                result.ErrorMessage = "Failed to load image";
                return result;
            }

            using (image)
            {
                // Check for cancellation before processing
                cancellationToken.ThrowIfCancellationRequested();

                // Process the image
                var processedImage = await ImageProcessingService.ProcessImageAsync(
                    image,
                    width,
                    height,
                    percent,
                    deltaX,
                    rigidity);

                if (processedImage == null)
                {
                    result.ErrorMessage = "Failed to process image";
                    return result;
                }

                using (processedImage)
                {
                    // Generate output path
                    var outputFileName = Path.GetFileNameWithoutExtension(inputPath) + "-cas" + Path.GetExtension(inputPath);
                    result.OutputPath = Path.Combine(outputFolder, outputFileName);

                    // Save the processed image
                    var saved = await ImageProcessingService.SaveImageAsync(processedImage, result.OutputPath);
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

    private static async Task<ProcessingResult> ProcessVideoFileAsync(
        string inputPath,
        string outputFolder,
        int? width,
        int? height,
        int? percent,
        double deltaX,
        double rigidity,
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

            // Extract frames from video using FFMediaToolkit
            var frames = await VideoProcessingService.ExtractFramesAsync(inputPath, cancellationToken);
            
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
            var videoOutputPath = Path.Combine(outputFolder, $"{videoName}-cas");
            
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

            // Process frames in parallel using the same pattern as image files
            var frameResults = await ProcessVideoFramesAsync(
                validFrames,
                videoOutputPath,
                videoName,
                width,
                height,
                percent,
                deltaX,
                rigidity,
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

    private static async Task<List<ProcessingResult>> ProcessVideoFramesAsync(
        List<VideoFrame> frames,
        string outputPath,
        string videoName,
        int? width,
        int? height,
        int? percent,
        double deltaX,
        double rigidity,
        ProgressBar frameProgressBar,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProcessingResult>();
        const int maxConcurrency = 8; // Use fewer threads for video frames to avoid memory issues

        // Timing and progress tracking for manual duration updates
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
        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var processingTasks = new List<Task<ProcessingResult>>();

        await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(cancellationToken);

            var task = Task.Run(async () => await ProcessSingleVideoFrameAsync(
                frame,
                outputPath,
                videoName,
                width,
                height,
                percent,
                deltaX,
                rigidity,
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

    private static async Task<ProcessingResult> ProcessSingleVideoFrameAsync(
        VideoFrame frame,
        string outputPath,
        string videoName,
        int? width,
        int? height,
        int? percent,
        double deltaX,
        double rigidity,
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
            
            // Validate frame data
            if (frame.Data == null || frame.Data.Length == 0)
            {
                result.ErrorMessage = "Frame has no data";
                completedCount.Increment();
                frameProgressBar.Tick($"Failed: frame-{frame.FrameIndex:D4}");
                return result;
            }

            if (frame.Width <= 0 || frame.Height <= 0)
            {
                result.ErrorMessage = $"Frame has invalid dimensions: {frame.Width}x{frame.Height}";
                completedCount.Increment();
                frameProgressBar.Tick($"Failed: frame-{frame.FrameIndex:D4}");
                return result;
            }

            // Convert video frame to MagickImage
            var image = await VideoProcessingService.ConvertFrameToMagickImageAsync(frame);
            if (image == null)
            {
                result.ErrorMessage = $"Failed to convert frame to image. Frame details: {frame.Width}x{frame.Height}, {frame.Data.Length} bytes, format: {frame.PixelFormat}";
                completedCount.Increment();
                frameProgressBar.Tick($"Failed: frame-{frame.FrameIndex:D4}");
                return result;
            }

            using (image)
            {
                // Validate converted image
                if (image.Width == 0 || image.Height == 0)
                {
                    result.ErrorMessage = "Converted image has zero dimensions";
                    completedCount.Increment();
                    frameProgressBar.Tick($"Failed: frame-{frame.FrameIndex:D4}");
                    return result;
                }

                // Process the frame
                var processedImage = await ImageProcessingService.ProcessImageAsync(
                    image,
                    width,
                    height,
                    percent,
                    deltaX,
                    rigidity);

                if (processedImage == null)
                {
                    result.ErrorMessage = $"Failed to process frame. Original size: {image.Width}x{image.Height}";
                    completedCount.Increment();
                    frameProgressBar.Tick($"Failed: frame-{frame.FrameIndex:D4}");
                    return result;
                }

                using (processedImage)
                {
                    // Validate processed image
                    if (processedImage.Width == 0 || processedImage.Height == 0)
                    {
                        result.ErrorMessage = "Processed image has zero dimensions";
                        completedCount.Increment();
                        frameProgressBar.Tick($"Failed: frame-{frame.FrameIndex:D4}");
                        return result;
                    }

                    // Generate frame output path
                    var frameFileName = $"{videoName}-frame-{frame.FrameIndex:D4}-cas.jpg";
                    var frameOutputPath = Path.Combine(outputPath, frameFileName);
                    result.OutputPath = frameOutputPath;

                    // Save the processed frame
                    var saved = await ImageProcessingService.SaveImageAsync(processedImage, frameOutputPath);
                    if (!saved)
                    {
                        result.ErrorMessage = $"Failed to save processed frame to {frameOutputPath}";
                        completedCount.Increment();
                        frameProgressBar.Tick($"Failed: frame-{frame.FrameIndex:D4}");
                        return result;
                    }

                    // Verify the saved file exists and has content
                    if (!File.Exists(frameOutputPath) || new FileInfo(frameOutputPath).Length == 0)
                    {
                        result.ErrorMessage = $"Saved frame file is missing or empty: {frameOutputPath}";
                        completedCount.Increment();
                        frameProgressBar.Tick($"Failed: frame-{frame.FrameIndex:D4}");
                        return result;
                    }

                    result.Success = true;
                }
            }

            // Update progress and calculate estimated duration after each completion
            var currentCompleted = completedCount.Increment();
            var elapsed = DateTime.Now - startTime;

            // Calculate and update estimated duration every few completions
            if (currentCompleted >= 3 && elapsed.TotalSeconds > 1) // Only estimate after we have some data
            {
                var throughput = currentCompleted / elapsed.TotalSeconds; // frames per second
                var remaining = totalFrames - currentCompleted;
                if (remaining > 0 && throughput > 0)
                {
                    var estimatedTimeRemaining = TimeSpan.FromSeconds(remaining / throughput);
                    frameProgressBar.EstimatedDuration = estimatedTimeRemaining;
                }
            }

            // Update progress bar
            frameProgressBar.Tick($"{(result.Success ? "Completed" : "Failed")}: frame-{frame.FrameIndex:D4}");
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
}

// Image processing service - handles all image-related operations
public static class ImageProcessingService
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".ico"
    };

    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mov", ".mkv", ".webm", ".wmv", ".flv", ".m4v"
    };

    public static bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return SupportedImageExtensions.Contains(extension);
    }

    public static bool IsVideoFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return SupportedVideoExtensions.Contains(extension);
    }

    public static bool IsMediaFile(string filePath)
    {
        return IsImageFile(filePath) || IsVideoFile(filePath);
    }

    public static async Task<MagickImage?> LoadImageAsync(string filePath)
    {
        if (!File.Exists(filePath) || !IsImageFile(filePath))
            return null;

        try
        {
            return await Task.Run(() => new MagickImage(filePath));
        }
        catch
        {
            return null;
        }
    }

    public static async Task<MagickImage?> ProcessImageAsync(
        MagickImage originalImage,
        int? targetWidth,
        int? targetHeight,
        int? scalePercent,
        double deltaX = 1.0,
        double rigidity = 0.0)
    {
        try
        {
            var processedImage = (MagickImage)originalImage.Clone();

            // Calculate dimensions based on provided parameters
            uint newWidth, newHeight;

            if (targetWidth.HasValue && targetHeight.HasValue)
            {
                newWidth = (uint)targetWidth.Value;
                newHeight = (uint)targetHeight.Value;
            }
            else if (scalePercent.HasValue)
            {
                var scale = scalePercent.Value / 100.0;
                newWidth = (uint)(originalImage.Width * scale);
                newHeight = (uint)(originalImage.Height * scale);
            }
            else
            {
                // Default to 50% if nothing specified
                newWidth = (uint)(originalImage.Width * 0.5);
                newHeight = (uint)(originalImage.Height * 0.5);
            }

            if (newWidth < 1) newWidth = 1;
            if (newHeight < 1) newHeight = 1;

            if (newWidth == originalImage.Width && newHeight == originalImage.Height)
            {
                return processedImage;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(30)); // Increased timeout for larger images

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        processedImage.LiquidRescale(newWidth, newHeight, deltaX, rigidity);
                    }
                    catch
                    {
                        // Fallback to regular resize
                        var geometry = new MagickGeometry(newWidth, newHeight)
                        {
                            IgnoreAspectRatio = true
                        };
                        processedImage.Resize(geometry);
                    }
                }, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout fallback
                var geometry = new MagickGeometry(newWidth, newHeight)
                {
                    IgnoreAspectRatio = true
                };
                processedImage.Resize(geometry);
            }

            return processedImage;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> SaveImageAsync(MagickImage image, string outputPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await Task.Run(() => image.Write(outputPath));
            return true;
        }
        catch
        {
            return false;
        }
    }
}

// Video processing service - handles all video-related operations
public static class VideoProcessingService
{
    private static bool _isInitialized = false;
    
    private static void InitializeFFmpeg()
    {
        if (_isInitialized) return;

        try
        {
            // Try to initialize FFMediaToolkit
            FFmpegLoader.FFmpegPath = GetFFmpegPath();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: FFmpeg initialization failed: {ex.Message}");
            Console.WriteLine("Please ensure FFmpeg is installed and available in your PATH or set FFMPEG_PATH environment variable.");
        }
    }
    
    private static string GetFFmpegPath()
    {
        // Find ffmpeg in PATH first
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var path in paths)
        {
            var ffmpegExecutable = Environment.OSVersion.Platform == PlatformID.Win32NT ? "ffmpeg.exe" : "ffmpeg";
            var fullPath = Path.Combine(path, ffmpegExecutable);
            if (File.Exists(fullPath))
            {
                return path;
            }
        }
        
        // Check environment variable first
        var envPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
        {
            return envPath;
        }
        
        // Check common library paths (FFMediaToolkit needs the lib directory, not bin)
        var commonPaths = new[]
        {
            "/opt/homebrew/opt/ffmpeg@7/lib",  // Homebrew FFmpeg 7.x
            "/opt/homebrew/lib",               // Homebrew default
            "/usr/local/lib",                  // Standard local libs
            "/usr/lib",                        // System libs
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "lib"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "lib")
        };
        
        foreach (var path in commonPaths)
        {
            // Check for essential FFmpeg libraries
            var libAvCodec = Path.Combine(path, Environment.OSVersion.Platform == PlatformID.Win32NT ? "avcodec.dll" : "libavcodec.dylib");
            var libAvFormat = Path.Combine(path, Environment.OSVersion.Platform == PlatformID.Win32NT ? "avformat.dll" : "libavformat.dylib");
            
            if (File.Exists(libAvCodec) && File.Exists(libAvFormat))
            {
                return path;
            }
        }
        
        // Return empty to let FFMediaToolkit try to find it
        return string.Empty;
    }

    public static async Task<List<VideoFrame>> ExtractFramesAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        var frames = new List<VideoFrame>();

        try
        {
            InitializeFFmpeg();

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
            // var maxFrames = Math.Min(frameCount, 10); // Test with 10 frames

            for (int i = 0; i < frameCount; i++)
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

    public static async Task<MagickImage?> ConvertFrameToMagickImageAsync(VideoFrame frame)
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
    
    private static byte[] ExtractCleanRGB24Data(byte[] sourceData, int width, int height, int stride)
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