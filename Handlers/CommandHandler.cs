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

            // Set default output path for single file
            if (string.IsNullOrEmpty(options.OutputPath))
            {
                var directory = Path.GetDirectoryName(options.InputPath) ?? string.Empty;
                var nameWithoutExt = Path.GetFileNameWithoutExtension(options.InputPath);
                var extension = Path.GetExtension(options.InputPath);

                if (options.Mode == ProcessingMode.Video)
                {
                    // Video: create folder with video name + "-cas"
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

        // Create output folder if needed (not for single image mode where outputPath is a file)
        if (options.Mode != ProcessingMode.SingleImage)
        {
            if (!Directory.Exists(options.OutputPath))
            {
                Directory.CreateDirectory(options.OutputPath);
            }
        }
        else
        {
            // For single image, ensure the output directory exists
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
