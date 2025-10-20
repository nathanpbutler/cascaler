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

        // Set default output path if not provided
        if (string.IsNullOrEmpty(options.OutputPath))
        {
            options.OutputPath = options.InputPath + Constants.OutputSuffix;
        }

        // Collect input files (both images and videos)
        var inputFiles = new List<string>();
        if (File.Exists(options.InputPath))
        {
            if (_imageService.IsMediaFile(options.InputPath))
            {
                inputFiles.Add(options.InputPath);
            }
            else
            {
                Console.WriteLine($"Error: Input file {options.InputPath} is not a supported media format.");
                return;
            }
        }
        else if (Directory.Exists(options.InputPath))
        {
            inputFiles.AddRange(Directory.GetFiles(options.InputPath)
                .Where(_imageService.IsMediaFile)
                .OrderBy(f => f));
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

        // Create output folder if it doesn't exist
        if (!Directory.Exists(options.OutputPath))
        {
            Directory.CreateDirectory(options.OutputPath);
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
