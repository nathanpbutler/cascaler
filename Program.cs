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
                    Percent = parseResult.GetValue<int>("--percent") == Constants.DefaultScalePercent
                        ? null
                        : parseResult.GetValue<int?>("--percent"),
                    DeltaX = parseResult.GetValue<double>("--deltaX"),
                    Rigidity = parseResult.GetValue<double>("--rigidity"),
                    MaxThreads = parseResult.GetValue<int>("--threads")
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
        services.AddSingleton<IMediaProcessor, MediaProcessor>();
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
            Description = "Percent of the output image (default 50)",
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

        return rootCommand;
    }
}
