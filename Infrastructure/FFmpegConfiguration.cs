using FFMediaToolkit;

namespace cascaler.Infrastructure;

/// <summary>
/// Handles FFmpeg library detection and initialization.
/// </summary>
public class FFmpegConfiguration
{
    private bool _isInitialized;

    /// <summary>
    /// Initializes FFmpeg by detecting and setting the library path.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            FFmpegLoader.FFmpegPath = GetFFmpegPath();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: FFmpeg initialization failed: {ex.Message}");
            Console.WriteLine("Please ensure FFmpeg is installed and available in your PATH or set FFMPEG_PATH environment variable.");
        }
    }

    /// <summary>
    /// Detects the FFmpeg installation path by checking PATH, environment variables, and common locations.
    /// </summary>
    private string GetFFmpegPath()
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

        // Check environment variable
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
}
