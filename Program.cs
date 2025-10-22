using System.CommandLine;
using cascaler.Handlers;
using cascaler.Infrastructure;
using cascaler.Models;
using cascaler.Services;
using cascaler.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace cascaler;

/// <summary>
/// Main program entry point with dependency injection setup.
/// </summary>
internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            // Setup dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            // Create and configure command-line interface
            var rootCommand = CreateRootCommand();

            // Create a cancellation token source for handling Ctrl+C
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\n\nCancellation requested...");
            };

            // Set command handler
            rootCommand.SetAction(async parseResult =>
            {
                var options = new ProcessingOptions
                {
                    InputPath = parseResult.GetValue<string>("input") ?? string.Empty,
                    OutputPath = parseResult.GetValue<string?>("--output"),
                    Width = parseResult.GetValue<int?>("--width"),
                    Height = parseResult.GetValue<int?>("--height"),
                    Percent = parseResult.GetValue<int>("--percent"),
                    StartWidth = parseResult.GetValue<int?>("--start-width"),
                    StartHeight = parseResult.GetValue<int?>("--start-height"),
                    StartPercent = parseResult.GetValue<int?>("--start-percent") ?? 100,
                    DeltaX = parseResult.GetValue<double>("--deltaX"),
                    Rigidity = parseResult.GetValue<double>("--rigidity"),
                    MaxThreads = parseResult.GetValue<int>("--threads"),
                    ShowProgress = !parseResult.GetValue<bool>("--no-progress"),
                    Format = parseResult.GetValue<string?>("--format"),
                    // TODO: Change these to support other time formats (e.g., hh:mm:ss, mm:ss, 1800ms etc.)
                    Start = parseResult.GetValue<double?>("--start"),
                    End = parseResult.GetValue<double?>("--end"),
                    Duration = parseResult.GetValue<double?>("--duration"),
                    Fps = parseResult.GetValue<int>("--fps")
                };

                var handler = serviceProvider.GetRequiredService<CommandHandler>();
                await handler.ExecuteAsync(options, cts.Token);
            });

            // Invoke the command
            return await rootCommand.Parse(args).InvokeAsync(cancellationToken: cts.Token);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Configures dependency injection services.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Configuration
        services.AddSingleton<ProcessingConfiguration>();
        services.AddSingleton<FFmpegConfiguration>();

        // Services
        services.AddSingleton<IImageProcessingService, ImageProcessingService>();
        services.AddSingleton<IVideoProcessingService, VideoProcessingService>();
        services.AddSingleton<IVideoCompilationService, VideoCompilationService>();
        services.AddSingleton<IMediaProcessor, MediaProcessor>();
        services.AddSingleton<IDimensionInterpolator, DimensionInterpolator>();
        services.AddTransient<IProgressTracker, ProgressTracker>();

        // Handlers
        services.AddTransient<CommandHandler>();
    }

    /// <summary>
    /// Creates and configures the root command with all options.
    /// </summary>
    private static RootCommand CreateRootCommand()
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
            Description = "Percent of the output image",
            Aliases = { "-p" },
            DefaultValueFactory = _ => Constants.DefaultScalePercent
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
            DefaultValueFactory = _ => Constants.DefaultImageThreads
        };

        var noProgressOption = new Option<bool>("--no-progress")
        {
            Description = "Disable progress bar output",
            DefaultValueFactory = _ => false
        };

        var outputOption = new Option<string?>("--output")
        {
            Description = "Output folder (default is input + \"-cas\")",
            Aliases = { "-o" }
        };

        // New options for gradual scaling and video features
        var formatOption = new Option<string?>("--format")
        {
            Description = "Output image format (png, jpg, bmp, tiff) (default is same as input for images, png for video frames)",
            Aliases = { "-f" }
        };

        var startOption = new Option<double?>("--start")
        {
            Description = "Start time in seconds for video trimming"
        };

        var endOption = new Option<double?>("--end")
        {
            Description = "End time in seconds for video trimming"
        };

        var durationOption = new Option<double?>("--duration")
        {
            Description = "Duration in seconds for image sequence generation or video trimming"
        };

        var startWidthOption = new Option<int?>("--start-width")
        {
            Description = "Start width for gradual scaling",
            Aliases = { "-sw" }
        };

        var startHeightOption = new Option<int?>("--start-height")
        {
            Description = "Start height for gradual scaling",
            Aliases = { "-sh" }
        };

        var startPercentOption = new Option<int?>("--start-percent")
        {
            Description = "Start percent for gradual scaling",
            Aliases = { "-sp" },
            DefaultValueFactory = _ => 100
        };

        var fpsOption = new Option<int>("--fps")
        {
            Description = "Frame rate for image-to-sequence conversion",
            DefaultValueFactory = _ => Constants.DefaultFps
        };

        var inputArgument = new Argument<string>("input")
        {
            Description = "Input image file or folder path"
        };

        // Add options and argument to root command
        rootCommand.Add(widthOption);
        rootCommand.Add(heightOption);
        rootCommand.Add(percentOption);
        rootCommand.Add(startWidthOption);
        rootCommand.Add(startHeightOption);
        rootCommand.Add(startPercentOption);
        rootCommand.Add(deltaXOption);
        rootCommand.Add(rigidityOption);
        rootCommand.Add(threadsOption);
        rootCommand.Add(noProgressOption);
        rootCommand.Add(outputOption);
        rootCommand.Add(formatOption);
        rootCommand.Add(startOption);
        rootCommand.Add(endOption);
        rootCommand.Add(durationOption);
        rootCommand.Add(fpsOption);
        rootCommand.Add(inputArgument);

        return rootCommand;
    }
}
