using cascaler.Infrastructure;
using cascaler.Models;
using cascaler.Services.Interfaces;

namespace cascaler.Handlers;

/// <summary>
/// Handles command-line processing and orchestrates media file processing.
/// </summary>
public class CommandHandler
{
    private readonly IImageProcessingService _imageService;
    private readonly IMediaProcessor _mediaProcessor;

    public CommandHandler(
        IImageProcessingService imageService,
        IMediaProcessor mediaProcessor)
    {
        _imageService = imageService;
        _mediaProcessor = mediaProcessor;
    }

    /// <summary>
    /// Executes the main processing workflow based on parsed command-line arguments.
    /// </summary>
    public async Task ExecuteAsync(ProcessingOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(options.InputPath))
        {
            Console.WriteLine("Error: Input path is required.");
            return;
        }

        // Validate that either width/height or percent is provided, not both
        if ((options.Width.HasValue || options.Height.HasValue) && options.Percent.HasValue && options.Percent != Constants.DefaultScalePercent)
        {
            Console.WriteLine("Error: Cannot specify both width/height and percent. Choose one scaling method.");
            return;
        }

        // Validate start dimensions don't mix width/height with percent
        if ((options.StartWidth.HasValue || options.StartHeight.HasValue) && options.StartPercent.HasValue && options.StartPercent != 100)
        {
            Console.WriteLine("Error: Cannot specify both start-width/start-height and start-percent. Choose one scaling method.");
            return;
        }

        // Validate output format
        if (!string.IsNullOrEmpty(options.Format) && !Constants.SupportedOutputFormats.Contains(options.Format))
        {
            Console.WriteLine($"Error: Unsupported output format '{options.Format}'. Supported formats: png, jpg, bmp, tiff");
            return;
        }

        // Validate FPS
        if (options.Fps <= 0)
        {
            Console.WriteLine("Error: FPS must be greater than 0.");
            return;
        }

        // Validate time parameters are positive
        if (options.Start.HasValue && options.Start.Value < 0)
        {
            Console.WriteLine("Error: Start time must be positive.");
            return;
        }

        if (options.End.HasValue && options.End.Value < 0)
        {
            Console.WriteLine("Error: End time must be positive.");
            return;
        }

        if (options.Duration.HasValue && options.Duration.Value <= 0)
        {
            Console.WriteLine("Error: Duration must be greater than 0.");
            return;
        }

        // Validate start < end
        if (options.Start.HasValue && options.End.HasValue && options.Start.Value >= options.End.Value)
        {
            Console.WriteLine("Error: Start time must be less than end time.");
            return;
        }

        // Validate cannot specify both end and duration
        if (options.End.HasValue && options.Duration.HasValue)
        {
            Console.WriteLine("Error: Cannot specify both end time and duration. Choose one.");
            return;
        }

        // Collect input files and determine processing mode
        var inputFiles = new List<string>();
        if (File.Exists(options.InputPath))
        {
            if (!_imageService.IsMediaFile(options.InputPath))
            {
                Console.WriteLine($"Error: Input file {options.InputPath} is not a supported media format.");
                return;
            }

            inputFiles.Add(options.InputPath);

            // Determine mode: single image or video
            if (_imageService.IsVideoFile(options.InputPath))
            {
                options.Mode = ProcessingMode.Video;
            }
            else
            {
                options.Mode = ProcessingMode.SingleImage;
            }

            // Mode-specific validations
            if (options.Mode == ProcessingMode.SingleImage)
            {
                // For single image with gradual scaling, duration is required
                if (options.IsGradualScaling && !options.Duration.HasValue)
                {
                    Console.WriteLine("Error: Duration must be specified for gradual scaling with a single image.");
                    return;
                }

                // Video trimming options don't make sense for images
                if ((options.Start.HasValue || options.End.HasValue) && !options.Duration.HasValue)
                {
                    Console.WriteLine("Error: Start/End time parameters are only valid for video files or with duration for image sequences.");
                    return;
                }
            }
            else if (options.Mode == ProcessingMode.Video)
            {
                // Video-specific validations
                // Duration without gradual scaling doesn't make sense for video (use end time instead)
                if (options.Duration.HasValue && !options.Start.HasValue && !options.IsGradualScaling)
                {
                    Console.WriteLine("Error: Duration for video trimming requires a start time. Use --start and --duration, or use --end instead.");
                    return;
                }
            }

            // Set default output path for single file
            if (string.IsNullOrEmpty(options.OutputPath))
            {
                var directory = Path.GetDirectoryName(options.InputPath) ?? string.Empty;
                var nameWithoutExt = Path.GetFileNameWithoutExtension(options.InputPath);
                var extension = Path.GetExtension(options.InputPath);

                if (options.Mode == ProcessingMode.Video || options.IsImageSequence)
                {
                    // Video or image sequence: create folder with name + "-cas"
                    options.OutputPath = Path.Combine(directory, nameWithoutExt + Constants.OutputSuffix);
                }
                else
                {
                    // Single image: same directory, modify filename
                    options.OutputPath = Path.Combine(directory, nameWithoutExt + Constants.OutputSuffix + extension);
                }
            }
        }
        else if (Directory.Exists(options.InputPath))
        {
            inputFiles.AddRange(Directory.GetFiles(options.InputPath)
                .Where(_imageService.IsMediaFile)
                .OrderBy(f => f));

            options.Mode = ProcessingMode.ImageBatch;

            // Batch mode validations
            if (options.Duration.HasValue || options.Start.HasValue || options.End.HasValue)
            {
                Console.WriteLine("Error: Duration, start, and end parameters are not supported for batch image processing.");
                return;
            }

            if (options.IsGradualScaling)
            {
                Console.WriteLine("Error: Gradual scaling is not supported for batch image processing.");
                return;
            }

            // Set default output path for folder
            if (string.IsNullOrEmpty(options.OutputPath))
            {
                options.OutputPath = options.InputPath + Constants.OutputSuffix;
            }
        }
        else
        {
            Console.WriteLine($"Error: Input path {options.InputPath} does not exist.");
            return;
        }

        if (inputFiles.Count == 0)
        {
            Console.WriteLine($"No supported media files found in {options.InputPath}");
            return;
        }

        // Create output folder if needed
        if (options.Mode != ProcessingMode.SingleImage || options.IsImageSequence)
        {
            // For video, batch, or image sequence: outputPath is a directory
            if (!Directory.Exists(options.OutputPath))
            {
                Directory.CreateDirectory(options.OutputPath);
            }
        }
        else
        {
            // For single image (non-sequence), ensure the output directory exists
            var outputDir = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
        }

        Console.WriteLine($"Processing {inputFiles.Count} media file(s) with {options.MaxThreads} thread(s)...");

        try
        {
            // Process files using MediaProcessor
            var results = await _mediaProcessor.ProcessMediaFilesAsync(
                inputFiles,
                options,
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
    }
}
