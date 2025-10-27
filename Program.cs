using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using nathanbutlerDEV.cascaler.Handlers;
using nathanbutlerDEV.cascaler.Infrastructure;
using nathanbutlerDEV.cascaler.Infrastructure.Options;
using nathanbutlerDEV.cascaler.Models;
using nathanbutlerDEV.cascaler.Services;
using nathanbutlerDEV.cascaler.Services.Interfaces;

namespace nathanbutlerDEV.cascaler;

/// <summary>
/// Main program entry point with dependency injection setup.
/// </summary>
internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            // Build configuration from embedded defaults and user config
            var configuration = ConfigurationHelper.BuildConfiguration();

            // Setup dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services, configuration);
            var serviceProvider = services.BuildServiceProvider();

            // Create and configure command-line interface
            var rootCommand = CreateRootCommand(configuration, serviceProvider);

            // Create a cancellation token source for handling Ctrl+C
            using var cts = new CancellationTokenSource();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                logger.LogInformation("Cancellation requested");
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
                    Percent = parseResult.GetValue<int?>("--percent"),
                    StartWidth = parseResult.GetValue<int?>("--start-width"),
                    StartHeight = parseResult.GetValue<int?>("--start-height"),
                    StartPercent = parseResult.GetValue<int?>("--start-percent"),
                    DeltaX = parseResult.GetValue<double>("--deltaX"),
                    Rigidity = parseResult.GetValue<double>("--rigidity"),
                    MaxThreads = parseResult.GetValue<int>("--threads"),
                    ShowProgress = !parseResult.GetValue<bool>("--no-progress"),
                    Format = parseResult.GetValue<string?>("--format"),
                    // TODO: Change these to support other time formats (e.g., hh:mm:ss, mm:ss, 1800ms etc.)
                    Start = parseResult.GetValue<double?>("--start"),
                    End = parseResult.GetValue<double?>("--end"),
                    Duration = parseResult.GetValue<double?>("--duration"),
                    Fps = parseResult.GetValue<int>("--fps"),
                    // Video encoding options
                    CRF = parseResult.GetValue<int?>("--crf"),
                    Preset = parseResult.GetValue<string?>("--preset"),
                    Codec = parseResult.GetValue<string?>("--codec"),
                    Vibrato = parseResult.GetValue<bool>("--vibrato"),
                    ScaleBack = parseResult.GetValue<bool>("--scale-back")
                };

                var handler = serviceProvider.GetRequiredService<CommandHandler>();
                await handler.ExecuteAsync(options, cts.Token);
            });

            // Invoke the command
            return await rootCommand.Parse(args).InvokeAsync(cancellationToken: cts.Token);
        }
        catch (Exception ex)
        {
            // Can't use logger here as DI may have failed, use Console.Error
            await Console.Error.WriteLineAsync($"Fatal error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Configures dependency injection services.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register configuration
        services.AddSingleton(configuration);

        // Configure logging
        ConfigureLogging(services, configuration);

        // Register and validate Options
        services.AddOptions<FFmpegOptions>()
            .Configure(options => configuration.GetSection("FFmpeg").Bind(options))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ProcessingSettings>()
            .Configure(options => configuration.GetSection("Processing").Bind(options))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<VideoEncodingOptions>()
            .Configure(options => configuration.GetSection("VideoEncoding").Bind(options))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OutputOptions>()
            .Configure(options => configuration.GetSection("Output").Bind(options))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Configuration classes
        services.AddSingleton<FFmpegConfiguration>();

        // Progress bar context (needed for logging integration)
        services.AddSingleton<IProgressBarContext, ProgressBarContext>();

        // Services
        services.AddSingleton<IImageProcessingService, ImageProcessingService>();
        services.AddSingleton<IVideoProcessingService, VideoProcessingService>();
        services.AddSingleton<IVideoCompilationService, VideoCompilationService>();
        services.AddSingleton<IMediaProcessor, MediaProcessor>();
        services.AddSingleton<IDimensionInterpolator, DimensionInterpolator>();
        services.AddTransient<IProgressTracker, ProgressTracker>();

        // Handlers
        services.AddTransient<CommandHandler>();
        services.AddTransient<ConfigCommandHandler>();
    }

    /// <summary>
    /// Configures logging with console and file providers.
    /// </summary>
    private static void ConfigureLogging(IServiceCollection services, IConfiguration configuration)
    {
        // Ensure log directory exists and cleanup old logs
        ConfigurationHelper.EnsureLogDirectoryExists();
        ConfigurationHelper.CleanupOldLogs(7);

        services.AddLogging(builder =>
        {
            // Add configuration from appsettings.json
            builder.AddConfiguration(configuration.GetSection("Logging"));

            // Add custom progress-bar-aware console logging - show info and above
            // This will coordinate with ShellProgressBar to prevent visual conflicts
            builder.Services.AddSingleton<ILoggerProvider>(serviceProvider =>
            {
                var progressBarContext = serviceProvider.GetRequiredService<IProgressBarContext>();
                return new ProgressBarAwareConsoleLoggerProvider(progressBarContext, LogLevel.Information);
            });

            // Add file logging - log everything (Debug and above)
            var logPath = Path.Combine(
                ConfigurationHelper.GetUserLogDirectory(),
                $"cascaler-{DateTime.Now:yyyyMMdd}.log"
            );
            builder.AddProvider(new FileLoggerProvider(logPath));

            // Set minimum level to Debug (will be filtered per provider above)
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    /// <summary>
    /// Creates and configures the root command with all options.
    /// </summary>
    private static RootCommand CreateRootCommand(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        var rootCommand = new RootCommand("cascaler - A high-performance batch liquid rescaling tool for images.");

        // Get configuration sections for default values
        var processingSettings = configuration.GetSection("Processing");
        var outputSettings = configuration.GetSection("Output");
        var videoEncodingSettings = configuration.GetSection("VideoEncoding");

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

        var percentOption = new Option<int?>("--percent")
        {
            Description = "Percent of the output image",
            Aliases = { "-p" },
            DefaultValueFactory = _ => processingSettings.GetValue<int?>("DefaultScalePercent")
        };

        var deltaXOption = new Option<double>("--deltaX")
        {
            Description = "Maximum seam transversal step (0 means straight seams - 1 means curved seams)",
            Aliases = { "-d" },
            DefaultValueFactory = _ => processingSettings.GetValue<double>("DefaultDeltaX")
        };

        var rigidityOption = new Option<double>("--rigidity")
        {
            Description = "Introduce a bias for non-straight seams (typically 0)",
            Aliases = { "-r" },
            DefaultValueFactory = _ => processingSettings.GetValue<double>("DefaultRigidity")
        };

        var threadsOption = new Option<int>("--threads")
        {
            Description = "Process images in parallel",
            Aliases = { "-t" },
            DefaultValueFactory = _ => processingSettings.GetValue<int>("MaxImageThreads")
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
            DefaultValueFactory = _ => processingSettings.GetValue<int?>("DefaultScalePercent") // Use same default as --percent
        };

        var fpsOption = new Option<int>("--fps")
        {
            Description = "Frame rate for image-to-sequence conversion",
            DefaultValueFactory = _ => processingSettings.GetValue<int>("DefaultFps")
        };

        // Video encoding options
        var crfOption = new Option<int?>("--crf")
        {
            Description = "Video encoding quality (0-51, lower is better quality, default from config: 23)"
        };

        var presetOption = new Option<string?>("--preset")
        {
            Description = "Video encoding preset (ultrafast|superfast|veryfast|faster|fast|medium|slow|slower|veryslow, default from config: medium)"
        };

        var codecOption = new Option<string?>("--codec")
        {
            Description = "Video codec (libx264|libx265, default from config: libx264)"
        };

        var vibratoOption = new Option<bool>("--vibrato")
        {
            Description = "Apply vibrato and tremolo audio effects (vibrato=d=1,tremolo) to video output",
            DefaultValueFactory = _ => processingSettings.GetValue<bool>("DefaultVibrato")
        };

        var scaleBackOption = new Option<bool>("--scale-back")
        {
            Description = "Scale processed frames back to original 100% dimensions (ignores start/end percent values)",
            DefaultValueFactory = _ => processingSettings.GetValue<bool>("DefaultScaleBack")
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
        rootCommand.Add(crfOption);
        rootCommand.Add(presetOption);
        rootCommand.Add(codecOption);
        rootCommand.Add(vibratoOption);
        rootCommand.Add(scaleBackOption);
        rootCommand.Add(inputArgument);

        // Create config subcommand
        var configCommand = new Command("config", "Manage configuration settings");

        // config show
        var showCommand = new Command("show", "Display the current effective configuration");
        showCommand.SetAction(parseResult =>
        {
            var handler = serviceProvider.GetRequiredService<ConfigCommandHandler>();
            handler.ShowConfig();
        });
        configCommand.Add(showCommand);

        // config path
        var pathCommand = new Command("path", "Show the path to the user configuration file");
        pathCommand.SetAction(parseResult =>
        {
            var handler = serviceProvider.GetRequiredService<ConfigCommandHandler>();
            handler.ShowConfigPath();
        });
        configCommand.Add(pathCommand);

        // config init
        var initCommand = new Command("init", "Create a user configuration file with current defaults");
        var initDetectFFmpegOption = new Option<bool>("--detect-ffmpeg")
        {
            Description = "Automatically detect and populate FFmpeg library path"
        };
        initCommand.Add(initDetectFFmpegOption);
        initCommand.SetAction(parseResult =>
        {
            var detectFFmpeg = parseResult.GetValue<bool>("--detect-ffmpeg");
            var handler = serviceProvider.GetRequiredService<ConfigCommandHandler>();
            handler.InitConfig(detectFFmpeg);
        });
        configCommand.Add(initCommand);

        // config export
        var exportCommand = new Command("export", "Export the current configuration to a file");
        var exportPathArgument = new Argument<string>("path")
        {
            Description = "Output file path"
        };
        var exportDetectFFmpegOption = new Option<bool>("--detect-ffmpeg")
        {
            Description = "Automatically detect and populate FFmpeg library path"
        };
        exportCommand.Add(exportPathArgument);
        exportCommand.Add(exportDetectFFmpegOption);
        exportCommand.SetAction(parseResult =>
        {
            var path = parseResult.GetValue<string>("path") ?? "config.json";
            var detectFFmpeg = parseResult.GetValue<bool>("--detect-ffmpeg");
            var handler = serviceProvider.GetRequiredService<ConfigCommandHandler>();
            handler.ExportConfig(path, detectFFmpeg);
        });
        configCommand.Add(exportCommand);

        rootCommand.Add(configCommand);

        return rootCommand;
    }
}
