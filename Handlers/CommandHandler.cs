using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using nathanbutlerDEV.cascaler.Infrastructure;
using nathanbutlerDEV.cascaler.Infrastructure.Options;
using nathanbutlerDEV.cascaler.Models;
using nathanbutlerDEV.cascaler.Services.Interfaces;

namespace nathanbutlerDEV.cascaler.Handlers;

/// <summary>
/// Handles command-line processing and orchestrates media file processing.
/// </summary>
public class CommandHandler
{
    private readonly IImageProcessingService _imageService;
    private readonly IMediaProcessor _mediaProcessor;
    private readonly ProcessingSettings _processingSettings;
    private readonly OutputOptions _outputOptions;
    private readonly ILogger<CommandHandler> _logger;

    public CommandHandler(
        IImageProcessingService imageService,
        IMediaProcessor mediaProcessor,
        IOptions<ProcessingSettings> processingSettings,
        IOptions<OutputOptions> outputOptions,
        ILogger<CommandHandler> logger)
    {
        _imageService = imageService;
        _mediaProcessor = mediaProcessor;
        _processingSettings = processingSettings.Value;
        _outputOptions = outputOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Executes the main processing workflow based on parsed command-line arguments.
    /// </summary>
    public async Task ExecuteAsync(ProcessingOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(options.InputPath))
        {
            _logger.LogError("Input path is required");
            return;
        }

        // Validate that either width/height or percent is provided, not both
        if ((options.Width.HasValue || options.Height.HasValue) && options.Percent.HasValue)
        {
            _logger.LogError("Cannot specify both width/height and percent. Choose one scaling method");
            return;
        }

        // Validate start dimensions don't mix width/height with percent
        if ((options.StartWidth.HasValue || options.StartHeight.HasValue) && options.StartPercent.HasValue && options.StartPercent != 100)
        {
            _logger.LogError("Cannot specify both start-width/start-height and start-percent. Choose one scaling method");
            return;
        }

        // Validate output format
        if (!string.IsNullOrEmpty(options.Format) && !Constants.SupportedOutputFormats.Contains(options.Format))
        {
            _logger.LogError("Unsupported output format '{Format}'. Supported formats: png, jpg, bmp, tiff", options.Format);
            return;
        }

        // Validate FPS
        if (options.Fps <= 0)
        {
            _logger.LogError("FPS must be greater than 0");
            return;
        }

        // Validate time parameters are positive
        if (options.Start.HasValue && options.Start.Value < 0)
        {
            _logger.LogError("Start time must be positive");
            return;
        }

        if (options.End.HasValue && options.End.Value < 0)
        {
            _logger.LogError("End time must be positive");
            return;
        }

        if (options.Duration.HasValue && options.Duration.Value <= 0)
        {
            _logger.LogError("Duration must be greater than 0");
            return;
        }

        // Validate start < end
        if (options.Start.HasValue && options.End.HasValue && options.Start.Value >= options.End.Value)
        {
            _logger.LogError("Start time must be less than end time");
            return;
        }

        // Validate cannot specify both end and duration
        if (options.End.HasValue && options.Duration.HasValue)
        {
            _logger.LogError("Cannot specify both end time and duration. Choose one");
            return;
        }

        // Collect input files and determine processing mode
        var inputFiles = new List<string>();
        if (File.Exists(options.InputPath))
        {
            if (!_imageService.IsMediaFile(options.InputPath))
            {
                _logger.LogError("Input file {InputPath} is not a supported media format", options.InputPath);
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
                    _logger.LogError("Duration must be specified for gradual scaling with a single image");
                    return;
                }

                // Video trimming options don't make sense for images
                if ((options.Start.HasValue || options.End.HasValue) && !options.Duration.HasValue)
                {
                    _logger.LogError("Start/End time parameters are only valid for video files or with duration for image sequences");
                    return;
                }
            }
            else if (options.Mode == ProcessingMode.Video)
            {
                // Video-specific validations
                // Duration without gradual scaling doesn't make sense for video (use end time instead)
                if (options.Duration.HasValue && !options.Start.HasValue && !options.IsGradualScaling)
                {
                    _logger.LogError("Duration for video trimming requires a start time. Use --start and --duration, or use --end instead");
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
                    options.OutputPath = Path.Combine(directory, nameWithoutExt + _outputOptions.Suffix);
                }
                else
                {
                    // Single image: same directory, modify filename
                    options.OutputPath = Path.Combine(directory, nameWithoutExt + _outputOptions.Suffix + extension);
                }
            }

            // Validate video output requirements
            if (options.IsVideoOutput)
            {
                // Video output only works with Video or ImageSequence modes
                if (options.Mode != ProcessingMode.Video && options.Mode != ProcessingMode.SingleImage)
                {
                    _logger.LogError("Video output (MP4/MKV) is only supported for video files or image sequences, not batch image processing");
                    return;
                }

                // Ensure video output extension is supported
                var outputExt = Path.GetExtension(options.OutputPath);
                if (!Constants.SupportedVideoOutputExtensions.Contains(outputExt))
                {
                    _logger.LogError("Unsupported video output extension '{OutputExt}'. Supported formats: .mp4, .mkv", outputExt);
                    return;
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
                _logger.LogError("Duration, start, and end parameters are not supported for batch image processing");
                return;
            }

            if (options.IsGradualScaling)
            {
                _logger.LogError("Gradual scaling is not supported for batch image processing");
                return;
            }

            // Set default output path for folder
            if (string.IsNullOrEmpty(options.OutputPath))
            {
                options.OutputPath = options.InputPath + _outputOptions.Suffix;
            }
        }
        else
        {
            _logger.LogError("Input path {InputPath} does not exist", options.InputPath);
            return;
        }

        if (inputFiles.Count == 0)
        {
            _logger.LogError("No supported media files found in {InputPath}", options.InputPath);
            return;
        }

        // Create output folder if needed
        if (options.IsVideoOutput)
        {
            // For video output, ensure the output directory exists (but don't create the video file itself)
            var outputDir = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }
        }
        else if (options.Mode != ProcessingMode.SingleImage || options.IsImageSequence)
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

        // _logger.LogInformation("Processing {FileCount} media file(s) with {ThreadCount} thread(s)", inputFiles.Count, options.MaxThreads);

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

            _logger.LogInformation("Processing complete: {SuccessCount} succeeded, {FailCount} failed", successCount, failCount);

            if (failCount > 0)
            {
                _logger.LogWarning("Failed files:");
                foreach (var failed in results.Where(r => !r.Success))
                {
                    _logger.LogWarning("  - {FileName}: {ErrorMessage}", Path.GetFileName(failed.InputPath), failed.ErrorMessage);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Operation cancelled by user");
        }
        finally
        {
            // Always restore cursor visibility
            Console.CursorVisible = true;
        }
    }
}
