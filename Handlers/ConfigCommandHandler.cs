using System.Text.Json;
using FFMediaToolkit;
using Microsoft.Extensions.Options;
using nathanbutlerDEV.cascaler.Infrastructure;
using nathanbutlerDEV.cascaler.Infrastructure.Options;

namespace nathanbutlerDEV.cascaler.Handlers;

/// <summary>
/// Handles configuration-related commands (show, init, export, path).
/// </summary>
public class ConfigCommandHandler
{
    private readonly IOptions<FFmpegOptions> _ffmpegOptions;
    private readonly IOptions<ProcessingSettings> _processingSettings;
    private readonly IOptions<VideoEncodingOptions> _videoEncodingOptions;
    private readonly IOptions<OutputOptions> _outputOptions;
    private readonly FFmpegConfiguration _ffmpegConfiguration;

    public ConfigCommandHandler(
        IOptions<FFmpegOptions> ffmpegOptions,
        IOptions<ProcessingSettings> processingSettings,
        IOptions<VideoEncodingOptions> videoEncodingOptions,
        IOptions<OutputOptions> outputOptions,
        FFmpegConfiguration ffmpegConfiguration)
    {
        _ffmpegOptions = ffmpegOptions;
        _processingSettings = processingSettings;
        _videoEncodingOptions = videoEncodingOptions;
        _outputOptions = outputOptions;
        _ffmpegConfiguration = ffmpegConfiguration;
    }

    /// <summary>
    /// Shows the current effective configuration.
    /// </summary>
    public void ShowConfig()
    {
        Console.WriteLine("Current Effective Configuration:");
        Console.WriteLine();

        Console.WriteLine("FFmpeg:");
        Console.WriteLine($"  LibraryPath: {_ffmpegOptions.Value.LibraryPath}");
        Console.WriteLine($"  EnableAutoDetection: {_ffmpegOptions.Value.EnableAutoDetection}");
        Console.WriteLine();

        Console.WriteLine("Processing:");
        Console.WriteLine($"  MaxImageThreads: {_processingSettings.Value.MaxImageThreads}");
        Console.WriteLine($"  MaxVideoThreads: {_processingSettings.Value.MaxVideoThreads}");
        Console.WriteLine($"  ProcessingTimeoutSeconds: {_processingSettings.Value.ProcessingTimeoutSeconds}");
        Console.WriteLine($"  MinimumItemsForETA: {_processingSettings.Value.MinimumItemsForETA}");
        Console.WriteLine($"  DefaultScalePercent: {_processingSettings.Value.DefaultScalePercent}");
        Console.WriteLine($"  DefaultFps: {_processingSettings.Value.DefaultFps}");
        Console.WriteLine($"  DefaultVideoFrameFormat: {_processingSettings.Value.DefaultVideoFrameFormat}");
        Console.WriteLine();

        Console.WriteLine("VideoEncoding:");
        Console.WriteLine($"  DefaultCRF: {_videoEncodingOptions.Value.DefaultCRF}");
        Console.WriteLine($"  DefaultPreset: {_videoEncodingOptions.Value.DefaultPreset}");
        Console.WriteLine($"  DefaultPixelFormat: {_videoEncodingOptions.Value.DefaultPixelFormat}");
        Console.WriteLine($"  DefaultCodec: {_videoEncodingOptions.Value.DefaultCodec}");
        Console.WriteLine();

        Console.WriteLine("Output:");
        Console.WriteLine($"  Suffix: {_outputOptions.Value.Suffix}");
        Console.WriteLine($"  ProgressCharacter: {_outputOptions.Value.ProgressCharacter}");
        Console.WriteLine($"  ShowEstimatedDuration: {_outputOptions.Value.ShowEstimatedDuration}");
    }

    /// <summary>
    /// Shows the path to the user configuration file.
    /// </summary>
    public void ShowConfigPath()
    {
        var configPath = ConfigurationHelper.GetUserConfigPath();
        var configDir = ConfigurationHelper.GetUserConfigDirectory();

        Console.WriteLine("User Configuration:");
        Console.WriteLine($"  Directory: {configDir}");
        Console.WriteLine($"  File: {configPath}");
        Console.WriteLine();

        if (File.Exists(configPath))
        {
            Console.WriteLine("Status: Configuration file exists");
            var fileInfo = new FileInfo(configPath);
            Console.WriteLine($"Last Modified: {fileInfo.LastWriteTime}");
        }
        else
        {
            Console.WriteLine("Status: Configuration file does not exist");
            Console.WriteLine($"Run 'cascaler config init' to create it");
        }
    }

    /// <summary>
    /// Initializes a user configuration file with current defaults.
    /// </summary>
    public void InitConfig(bool detectFFmpeg = false)
    {
        var configPath = ConfigurationHelper.GetUserConfigPath();
        var configDir = ConfigurationHelper.GetUserConfigDirectory();

        // Check if config already exists
        if (File.Exists(configPath))
        {
            Console.Write($"Configuration file already exists at {configPath}. Overwrite? (y/N): ");
            var response = Console.ReadLine()?.Trim().ToLower();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("Operation cancelled.");
                return;
            }
        }

        // Create directory if it doesn't exist
        Directory.CreateDirectory(configDir);

        // Detect FFmpeg path if requested
        string ffmpegPath = _ffmpegOptions.Value.LibraryPath;
        if (detectFFmpeg)
        {
            Console.WriteLine("Detecting FFmpeg library path...");
            _ffmpegConfiguration.Initialize();
            ffmpegPath = GetDetectedFFmpegPath();
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                Console.WriteLine($"FFmpeg detected at: {ffmpegPath}");
            }
            else
            {
                Console.WriteLine("FFmpeg not detected. You can set the path manually in the config file.");
            }
        }

        // Create configuration object with current values
        var config = new
        {
            FFmpeg = new
            {
                LibraryPath = ffmpegPath,
                EnableAutoDetection = _ffmpegOptions.Value.EnableAutoDetection
            },
            Processing = new
            {
                MaxImageThreads = _processingSettings.Value.MaxImageThreads,
                MaxVideoThreads = _processingSettings.Value.MaxVideoThreads,
                ProcessingTimeoutSeconds = _processingSettings.Value.ProcessingTimeoutSeconds,
                MinimumItemsForETA = _processingSettings.Value.MinimumItemsForETA,
                DefaultScalePercent = _processingSettings.Value.DefaultScalePercent,
                DefaultFps = _processingSettings.Value.DefaultFps,
                DefaultVideoFrameFormat = _processingSettings.Value.DefaultVideoFrameFormat
            },
            VideoEncoding = new
            {
                DefaultCRF = _videoEncodingOptions.Value.DefaultCRF,
                DefaultPreset = _videoEncodingOptions.Value.DefaultPreset,
                DefaultPixelFormat = _videoEncodingOptions.Value.DefaultPixelFormat,
                DefaultCodec = _videoEncodingOptions.Value.DefaultCodec
            },
            Output = new
            {
                Suffix = _outputOptions.Value.Suffix,
                ProgressCharacter = _outputOptions.Value.ProgressCharacter,
                ShowEstimatedDuration = _outputOptions.Value.ShowEstimatedDuration
            }
        };

        // Serialize to JSON with nice formatting
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(configPath, json);

        Console.WriteLine($"Configuration file created at: {configPath}");
        Console.WriteLine();
        Console.WriteLine("You can now edit this file to customize your settings.");
    }

    /// <summary>
    /// Exports the current configuration to a specified file.
    /// </summary>
    public void ExportConfig(string outputPath, bool detectFFmpeg = false)
    {
        // Ensure the output path has .json extension
        if (!outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            outputPath += ".json";
        }

        // Check if file already exists
        if (File.Exists(outputPath))
        {
            Console.Write($"File already exists at {outputPath}. Overwrite? (y/N): ");
            var response = Console.ReadLine()?.Trim().ToLower();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("Operation cancelled.");
                return;
            }
        }

        // Detect FFmpeg path if requested
        string ffmpegPath = _ffmpegOptions.Value.LibraryPath;
        if (detectFFmpeg)
        {
            Console.WriteLine("Detecting FFmpeg library path...");
            _ffmpegConfiguration.Initialize();
            ffmpegPath = GetDetectedFFmpegPath();
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                Console.WriteLine($"FFmpeg detected at: {ffmpegPath}");
            }
            else
            {
                Console.WriteLine("FFmpeg not detected. You can set the path manually in the config file.");
            }
        }

        // Create configuration object with current values
        var config = new
        {
            FFmpeg = new
            {
                LibraryPath = ffmpegPath,
                EnableAutoDetection = _ffmpegOptions.Value.EnableAutoDetection
            },
            Processing = new
            {
                MaxImageThreads = _processingSettings.Value.MaxImageThreads,
                MaxVideoThreads = _processingSettings.Value.MaxVideoThreads,
                ProcessingTimeoutSeconds = _processingSettings.Value.ProcessingTimeoutSeconds,
                MinimumItemsForETA = _processingSettings.Value.MinimumItemsForETA,
                DefaultScalePercent = _processingSettings.Value.DefaultScalePercent,
                DefaultFps = _processingSettings.Value.DefaultFps,
                DefaultVideoFrameFormat = _processingSettings.Value.DefaultVideoFrameFormat
            },
            VideoEncoding = new
            {
                DefaultCRF = _videoEncodingOptions.Value.DefaultCRF,
                DefaultPreset = _videoEncodingOptions.Value.DefaultPreset,
                DefaultPixelFormat = _videoEncodingOptions.Value.DefaultPixelFormat,
                DefaultCodec = _videoEncodingOptions.Value.DefaultCodec
            },
            Output = new
            {
                Suffix = _outputOptions.Value.Suffix,
                ProgressCharacter = _outputOptions.Value.ProgressCharacter,
                ShowEstimatedDuration = _outputOptions.Value.ShowEstimatedDuration
            }
        };

        // Serialize to JSON with nice formatting
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(outputPath, json);

        Console.WriteLine($"Configuration exported to: {outputPath}");
    }

    /// <summary>
    /// Gets the detected FFmpeg path from FFmpegLoader after initialization.
    /// </summary>
    private string GetDetectedFFmpegPath()
    {
        try
        {
            // Access the FFmpegLoader.FFmpegPath property which was set during Initialize()
            return FFmpegLoader.FFmpegPath ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
