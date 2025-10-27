using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using nathanbutlerDEV.cascaler.Infrastructure.Options;

namespace nathanbutlerDEV.cascaler.Infrastructure;

/// <summary>
/// Handles FFmpeg library detection and initialization.
/// </summary>
public class FFmpegConfiguration
{
    private readonly FFmpegOptions _options;
    private readonly ILogger<FFmpegConfiguration> _logger;
    private bool _isInitialized;
    private string? _cachedPath;

    public FFmpegConfiguration(IOptions<FFmpegOptions> options, ILogger<FFmpegConfiguration> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Initializes FFmpeg by detecting and setting the library path.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            var ffmpegPath = GetFFmpegPath();
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                ffmpeg.RootPath = ffmpegPath;
                _logger.LogDebug("FFmpeg.AutoGen initialized with path: {Path}", ffmpegPath);
            }
            else
            {
                _logger.LogDebug("FFmpeg.AutoGen using default path resolution");
            }

            // Test FFmpeg availability by getting version
            var version = ffmpeg.av_version_info();
            _logger.LogInformation("FFmpeg version: {Version}", version);

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FFmpeg initialization failed");
            _logger.LogInformation("Please ensure FFmpeg is installed or configure LibraryPath in ~/.config/cascaler/appsettings.json");
        }
    }

    /// <summary>
    /// Detects the FFmpeg installation path by checking configuration, PATH, environment variables, and common locations.
    /// </summary>
    private string GetFFmpegPath()
    {
        // Return cached path if available
        if (!string.IsNullOrEmpty(_cachedPath))
        {
            return _cachedPath;
        }

        // Check configured library path first (highest priority)
        if (!string.IsNullOrEmpty(_options.LibraryPath) && Directory.Exists(_options.LibraryPath))
        {
            _cachedPath = _options.LibraryPath;
            return _cachedPath;
        }

        // If auto-detection is disabled and no valid path configured, return empty
        if (!_options.EnableAutoDetection)
        {
            return string.Empty;
        }

        // Check environment variable (second priority)
        var envPath = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
        {
            _cachedPath = envPath;
            return _cachedPath;
        }

        // Check common library paths (FFmpeg.AutoGen needs the lib directory, not bin)
        var commonPaths = new[]
        {
            "/opt/homebrew/opt/ffmpeg@7/lib",  // Apple Silicon Homebrew FFmpeg 7.x
            "/usr/local/opt/ffmpeg@7/lib",     // Intel Mac Homebrew FFmpeg 7.x
            "/opt/homebrew/opt/ffmpeg/lib",    // Homebrew FFmpeg (current version)
            "/opt/homebrew/lib",               // Homebrew default
            "/usr/local/lib",                  // Standard local libs
            "/usr/lib",                        // System libs
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "lib"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "lib")
        };

        foreach (var path in commonPaths)
        {
            if (HasEssentialLibraries(path))
            {
                _cachedPath = path;
                return _cachedPath;
            }
        }

        // Find ffmpeg in PATH and derive lib directory
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var path in paths)
        {
            var ffmpegExecutable = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
            var fullPath = Path.Combine(path, ffmpegExecutable);
            if (File.Exists(fullPath))
            {
                // FFmpeg.AutoGen needs the lib directory, not the bin directory
                // Try to find sibling lib directory (e.g., /opt/homebrew/bin -> /opt/homebrew/lib)
                var parentDir = Directory.GetParent(path)?.FullName;
                if (parentDir != null)
                {
                    var libDir = Path.Combine(parentDir, "lib");
                    if (HasEssentialLibraries(libDir))
                    {
                        _cachedPath = libDir;
                        return _cachedPath;
                    }
                }
            }
        }

        // Return empty to let FFmpeg.AutoGen try to find it
        _cachedPath = string.Empty;
        return _cachedPath;
    }

    /// <summary>
    /// Checks if a directory contains essential FFmpeg libraries.
    /// </summary>
    private bool HasEssentialLibraries(string path)
    {
        if (!Directory.Exists(path))
            return false;

        if (OperatingSystem.IsWindows())
        {
            return File.Exists(Path.Combine(path, "avcodec.dll")) &&
                   File.Exists(Path.Combine(path, "avformat.dll"));
        }
        else
        {
            // On Unix (macOS/Linux), libraries have version suffixes (e.g., libavcodec.62.dylib)
            var files = Directory.GetFiles(path);
            var extension = OperatingSystem.IsMacOS() ? ".dylib" : ".so";

            var hasAvCodec = files.Any(f =>
                Path.GetFileName(f).StartsWith("libavcodec.") && f.EndsWith(extension));
            var hasAvFormat = files.Any(f =>
                Path.GetFileName(f).StartsWith("libavformat.") && f.EndsWith(extension));

            return hasAvCodec && hasAvFormat;
        }
    }
}
