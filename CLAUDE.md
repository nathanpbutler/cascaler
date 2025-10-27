# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**cascaler** is a high-performance batch liquid rescaling tool for images and videos using content-aware seam carving (liquid rescaling). Built with .NET 9.0, it processes media files in parallel using ImageMagick for liquid rescaling and FFMediaToolkit for video decoding, encoding, and frame extraction.

## Build and Run Commands

```bash
# Build the project
dotnet build

# Run the application
dotnet run -- [options] <input>

# Build and publish a release build
dotnet publish -c Release

# Example: Process a single image with default 50% scale
dotnet run -- input.jpg

# Example: Process folder with custom dimensions
dotnet run -- -w 800 -h 600 /path/to/images

# Example: Process with percentage and multiple threads
dotnet run -- -p 75 -t 8 /path/to/folder

# Example: Process a video file (extracts and processes frames)
dotnet run -- input.mp4 -o output_folder

# Example: Gradual scaling on image (generates sequence from 100% to 50% over 3 seconds)
dotnet run -- input.jpg --duration 3 -sp 100 -p 50

# Example: Gradual scaling on video with format override
dotnet run -- input.mp4 -sp 100 -p 50 -f jpg

# Example: Trim video and apply gradual scaling
dotnet run -- input.mp4 --start 10 --duration 5 -sp 100 -p 50

# Example: Generate 30fps image sequence with custom dimensions
dotnet run -- input.jpg --duration 2 --fps 30 -w 1920 -h 1080

# Example: Output directly to MP4 video file with audio preservation
dotnet run -- input.mp4 -o output.mp4 -p 75

# Example: Image sequence to video file
dotnet run -- input.jpg --duration 5 -o output.mp4 -sp 100 -p 50
```

## Command-Line Options

### Basic Scaling Options

- `--width` / `-w`: Target width in pixels
- `--height` / `-h`: Target height in pixels
- `--percent` / `-p`: Scale percentage (default: 50)
- `--deltaX` / `-d`: Seam transversal step (0=straight, 1=curved, default: 1.0)
- `--rigidity` / `-r`: Bias for non-straight seams (default: 1.0)
- `--threads` / `-t`: Parallel processing threads (default: 16)
- `--output` / `-o`: Output path (default: input path/name + "-cas" suffix)
- `--no-progress`: Disable progress bar and progress updates (shows only important log messages)

### Gradual Scaling Options

- `--start-width` / `-sw`: Start width for gradual scaling
- `--start-height` / `-sh`: Start height for gradual scaling
- `--start-percent` / `-sp`: Start percent for gradual scaling (default: 100)

### Video & Sequence Options

- `--format` / `-f`: Output image format (png, jpg, bmp, tiff) - default: input format for images, png for video frames (ignored when output is .mp4/.mkv)
- `--start`: Start time in seconds for video trimming
- `--end`: End time in seconds for video trimming
- `--duration`: Duration in seconds for image sequence generation or video trimming
- `--fps`: Frame rate for image-to-sequence conversion (default: 25)

**Note:** When the output path has a `.mp4` or `.mkv` extension, frames are automatically compiled into a video file with H.264 encoding. Audio is preserved from source videos.

### Validation Rules

- Cannot specify both width/height and percent - choose one scaling method
- Cannot specify both start-width/height and start-percent - choose one scaling method
- Cannot specify both `--end` and `--duration` for video trimming
- Single image with gradual scaling requires `--duration` to determine frame count
- Batch mode does not support duration, start, end, or gradual scaling parameters
- Video output (.mp4/.mkv) is only supported for video files or image sequences, not batch image processing

## Configuration

### User Configuration File

cascaler supports persistent configuration via a user config file. The application looks for configuration in:

- **Unix/Linux/macOS**: `~/.config/cascaler/appsettings.json`
- **Windows**: `%APPDATA%\cascaler\appsettings.json`

### Configuration Management Commands

The `config` subcommand provides tools for managing configuration:

```bash
# Show current effective configuration (merged from all sources)
cascaler config show

# Show path to user configuration file
cascaler config path

# Create user configuration file with current defaults
cascaler config init

# Create config file with automatic FFmpeg path detection
cascaler config init --detect-ffmpeg

# Export configuration to a specific file
cascaler config export my-config.json

# Export configuration with automatic FFmpeg path detection
cascaler config export my-config.json --detect-ffmpeg
```

**Usage Notes:**

- `config show` displays the effective configuration after merging defaults, user config, and CLI args
- `config init` creates a user config file that you can edit to customize defaults
- `config init --detect-ffmpeg` automatically detects and populates the FFmpeg library path
- `config export` is useful for creating configuration templates or backing up settings
- `config export --detect-ffmpeg` exports with automatic FFmpeg path detection
- `config path` shows where to place your configuration file

### Configuration Priority

Settings are applied in the following order (later overrides earlier):

1. **Embedded defaults** - Built into the executable
2. **User config file** - Optional file in user's config directory
3. **Command-line arguments** - Always take precedence

### Configuration Sections

#### FFmpeg

```json
{
  "FFmpeg": {
    "LibraryPath": "",           // Path to FFmpeg libraries (empty = auto-detect)
    "EnableAutoDetection": true  // Enable automatic FFmpeg detection
  }
}
```

**Configuration Behavior:**

- `LibraryPath`: Specifies the FFmpeg library directory
  - Use `cascaler config init --detect-ffmpeg` to automatically populate this value
  - If the specified path doesn't exist, falls back to auto-detection (if `EnableAutoDetection` is `true`)
  - Leave empty to always use auto-detection

- `EnableAutoDetection`: Controls fallback behavior (default: `true`)
  - When `true`: If `LibraryPath` is invalid or empty, automatically searches for FFmpeg in standard locations
  - When `false`: Only uses the configured `LibraryPath`, fails if invalid/empty
  - **Recommended:** Keep as `true` for portability across different machines and FFmpeg versions

**Benefits**:

- Configuring `LibraryPath` avoids runtime detection on every execution, improving startup performance
- Keeping `EnableAutoDetection: true` provides smart fallback if the configured path becomes invalid

#### Processing

```json
{
  "Processing": {
    "MaxImageThreads": 16,              // Parallel threads for image processing
    "MaxVideoThreads": 8,               // Parallel threads for video frame processing
    "ProcessingTimeoutSeconds": 30,     // Timeout for liquid rescale operations
    "MinimumItemsForETA": 3,            // Min items before showing ETA
    "DefaultScalePercent": 50,          // Default scale percentage
    "DefaultFps": 25,                   // Default FPS for sequences
    "DefaultVideoFrameFormat": "png"    // Default format for video frames
  }
}
```

#### VideoEncoding

```json
{
  "VideoEncoding": {
    "DefaultCRF": 23,               // H.264 quality (0-51, lower = better)
    "DefaultPreset": "medium",      // Encoding speed preset
    "DefaultPixelFormat": "yuv420p", // Pixel format for compatibility
    "DefaultCodec": "libx264"       // Video codec
  }
}
```

#### Output

```json
{
  "Output": {
    "Suffix": "-cas",                 // Output file/folder suffix
    "ProgressCharacter": "─",         // Progress bar character
    "ShowEstimatedDuration": true     // Show ETA in progress bar
  }
}
```

### Example User Configuration

Create `~/.config/cascaler/appsettings.json` (Unix) or `%APPDATA%\cascaler\appsettings.json` (Windows):

```json
{
  "FFmpeg": {
    "LibraryPath": "/opt/homebrew/opt/ffmpeg@7/lib"
  },
  "Processing": {
    "MaxImageThreads": 32,
    "DefaultScalePercent": 75
  },
  "VideoEncoding": {
    "DefaultCRF": 20
  },
  "Output": {
    "Suffix": "-scaled"
  }
}
```

## Logging

cascaler uses **Microsoft.Extensions.Logging** for diagnostic output with a dual-output strategy optimized for CLI applications. The logging system is fully integrated with ShellProgressBar to prevent visual conflicts.

### Log Locations

- **Console Output**: Clean, user-facing messages (Information level and above)
- **File Logs**: Detailed diagnostic information at `~/.config/cascaler/logs/cascaler-YYYYMMDD.log` (Unix) or `%APPDATA%\cascaler\logs\cascaler-YYYYMMDD.log` (Windows)

### Log Retention

Log files are automatically cleaned up after **7 days**. Cleanup occurs on application startup via `ConfigurationHelper.CleanupOldLogs()`.

### Console Output Behavior

The application uses a **progress-bar-aware logging system** that coordinates console output with ShellProgressBar:

- **Information logs**: Clean messages without prefixes (e.g., `Processing 10 files...`)
- **Warning logs**: Prefixed with `[WARN]` and category name for context
- **Error logs**: Prefixed with `[ERROR]` and category name, includes exception details
- **Progress bar mode**: Logging output is routed through `progressBar.WriteLine()` to prevent visual conflicts
- **No progress mode**: Logging output goes directly to console

**Example Console Output (with progress bar):**

```plaintext
Processing video: input.mp4 (977.3 MB)
Successfully extracted 300 frames from video
Starting unified video encoder for 300 frames at 50 fps
100.00% Completed: input.mov                                      00:00:20 / 00:00:00
────────────────────────────────────────────────────────────────────────────────────
Processing complete: 1 succeeded, 0 failed
```

**Example Console Output (with `--no-progress`):**

```plaintext
Processing 1 media file(s) with 16 thread(s)
Processing video: input.mp4 (977.3 MB)
Successfully extracted 300 frames from video
Starting unified video encoder for 300 frames at 50 fps
Video encoding completed: 300 frames written
Processing complete: 1 succeeded, 0 failed
```

**With warnings:**

```plaintext
[WARN] nathanbutlerDEV.cascaler.Services.VideoProcessingService: 5 frames were invalid and will be skipped
```

### File Logging

```plaintext
2025-10-27 13:40:05.123 [Debug] nathanbutlerDEV.cascaler.Infrastructure.FFmpegConfiguration: FFmpeg path detection starting
2025-10-27 13:40:05.456 [Information] nathanbutlerDEV.cascaler.Handlers.CommandHandler: Processing 1 media file(s) with 16 thread(s)
2025-10-27 13:40:10.789 [Warning] nathanbutlerDEV.cascaler.Services.VideoProcessingService: 5 frames were invalid and will be skipped
```

File logs capture **everything** (Debug, Information, Warning, Error, Critical) with full context:

```plaintext
2025-10-27 13:40:05.123 [Debug] nathanbutlerDEV.cascaler.Infrastructure.FFmpegConfiguration: FFmpeg path detection starting
2025-10-27 13:40:05.456 [Information] nathanbutlerDEV.cascaler.Handlers.CommandHandler: Processing 1 media file(s) with 16 thread(s)
2025-10-27 13:40:10.789 [Warning] nathanbutlerDEV.cascaler.Services.VideoProcessingService: 5 frames were invalid and will be skipped
```

### Progress Bar Integration

The logging system uses a custom implementation that coordinates with ShellProgressBar:

**Architecture:**
- `IProgressBarContext`: Singleton service that tracks the active progress bar
- `ProgressBarAwareConsoleLogger`: Custom logger that routes output through progress bar when active
- `ProgressBarAwareConsoleLoggerProvider`: Logger provider that creates progress-bar-aware loggers

**Behavior:**
- When progress bar is active: All console logging goes through `progressBar.WriteLine()` to prevent conflicts
- When `--no-progress` is used: No progress bar created, no progress updates shown, only important log messages displayed
- File logging is unaffected by progress bar state

### Implementation Details

**Custom Components:**

- `ProgressBarContext`: Tracks active progress bar in `Infrastructure/ProgressBarContext.cs`
- `ProgressBarAwareConsoleLogger`: Progress-bar-aware logger in `Infrastructure/ProgressBarAwareConsoleLogger.cs`
- `ProgressBarAwareConsoleLoggerProvider`: Logger provider in `Infrastructure/ProgressBarAwareConsoleLoggerProvider.cs`
- `FileLoggerProvider`: Custom file logger in `Infrastructure/FileLoggerProvider.cs`
- `ProgressTracker`: Centralized progress tracking (no output when `--no-progress`)

**Configuration** (in `Program.cs`):

```csharp
// Progress bar context for logging integration
services.AddSingleton<IProgressBarContext, ProgressBarContext>();

// Console: Information and above with progress-bar-aware logger
builder.Services.AddSingleton<ILoggerProvider>(serviceProvider =>
{
    var progressBarContext = serviceProvider.GetRequiredService<IProgressBarContext>();
    return new ProgressBarAwareConsoleLoggerProvider(progressBarContext, LogLevel.Information);
});

// File: Debug and above with full details
builder.AddProvider(new FileLoggerProvider(logPath));
builder.SetMinimumLevel(LogLevel.Debug);
```

**Structured Logging:**
All log messages use structured logging with named parameters for better searchability:

```csharp
_logger.LogInformation("Processing {FileCount} files with {ThreadCount} threads", fileCount, threadCount);
```

## Output Naming Conventions

The `-cas` suffix is applied **once** to output paths, with behavior varying by input type:

| Input Type | Input Example | Default Output | Notes |
|------------|---------------|----------------|-------|
| Single Image | `/path/to/image.png` | `/path/to/image-cas.png` | Suffix added to filename |
| Single Image (no gradual scaling) | `/path/to/image.png` | `/path/to/image-cas.png` | Same dimensions as input (liquid rescaled) |
| Image Sequence | `/path/to/image.jpg --duration 3` | `/path/to/image-cas/frame-0001.png` | Generates multiple frames in folder |
| Image Sequence to Video | `/path/to/image.jpg --duration 3 -o video.mp4` | `/path/to/video.mp4` | Direct video output |
| Image Folder | `/path/to/folder/` | `/path/to/folder-cas/image.png` | Suffix added to folder name only |
| Video File (frame output) | `/path/to/video.mp4` | `/path/to/video-cas/frame-0001.png` | Suffix added to output folder name |
| Video File (video output) | `/path/to/video.mp4 -o output.mp4` | `/path/to/output.mp4` | Direct video output with audio |

**Frame Naming:** When outputting to individual image files (not video), frames are named sequentially as `frame-0001.png`, `frame-0002.png`, etc. Default format is PNG for video/sequence frames, can be overridden with `--format`.

**Frame Count Calculation:**

- Image sequence: `duration × fps` (e.g., 3 seconds × 25 fps = 75 frames)
- Video (full): All frames from video
- Video (trimmed): `(end - start) × video_fps` or `duration × video_fps`

**Video Output Format:**

- Supported containers: `.mp4` (recommended), `.mkv`
- Video codec: H.264 (libx264) with CRF 23 (high quality)
- Audio: Automatically extracted and preserved from source videos
- Audio trimming: Audio is synchronized with frame trimming when using `--start`/`--end`/`--duration`
- Container selection: MP4 is used for AAC/MP3/AC3 audio; MKV for other codecs

## Architecture

### Project Structure

The application uses a clean, layered architecture with dependency injection for maintainability and testability:

```plaintext
cascaler/
├── Program.cs                          # Entry point with DI setup & configuration
├── appsettings.json                    # Default configuration (embedded in executable)
├── Models/
│   ├── ProcessingOptions.cs           # CLI argument encapsulation + ProcessingMode enum
│   ├── ProcessingResult.cs            # Processing outcome data
│   └── VideoFrame.cs                  # Video frame metadata
├── Services/
│   ├── Interfaces/
│   │   ├── IImageProcessingService.cs
│   │   ├── IVideoProcessingService.cs
│   │   ├── IVideoCompilationService.cs
│   │   ├── IMediaProcessor.cs
│   │   ├── IProgressTracker.cs
│   │   └── IDimensionInterpolator.cs
│   ├── ImageProcessingService.cs      # ImageMagick operations
│   ├── VideoProcessingService.cs      # FFMediaToolkit integration & trimming
│   ├── VideoCompilationService.cs     # Video encoding & audio handling via FFMediaToolkit
│   ├── MediaProcessor.cs              # Batch processing orchestration
│   ├── ProgressTracker.cs             # Centralized ETA calculation
│   └── DimensionInterpolator.cs       # Gradual scaling dimension calculation
├── Infrastructure/
│   ├── Constants.cs                           # Immutable constants (file extensions, codec lists)
│   ├── ConfigurationHelper.cs                 # Multi-source configuration builder
│   ├── FFmpegConfiguration.cs                 # FFmpeg path detection with caching
│   ├── IProgressBarContext.cs                 # Interface for progress bar context
│   ├── ProgressBarContext.cs                  # Tracks active progress bar for logging integration
│   ├── ProgressBarAwareConsoleLogger.cs       # Logger that coordinates with progress bar
│   ├── ProgressBarAwareConsoleLoggerProvider.cs # Provider for progress-bar-aware loggers
│   ├── FileLoggerProvider.cs                  # Custom file logging provider
│   └── Options/
│       ├── FFmpegOptions.cs                   # FFmpeg configuration settings
│       ├── ProcessingSettings.cs              # Processing configuration with validation
│       ├── VideoEncodingOptions.cs            # Video encoding settings
│       └── OutputOptions.cs                   # Output formatting settings
├── Handlers/
│   └── CommandHandler.cs              # CLI command orchestration
└── Utilities/
    ├── SharedCounter.cs               # Thread-safe counter
    └── FrameOrderingBuffer.cs         # Frame reordering for parallel processing

```

### Key Components

1. **Program.cs**: Entry point with configuration and dependency injection
    - Builds layered configuration from embedded defaults + user config
    - Registers Options classes with validation
    - Configures command-line interface with System.CommandLine
    - Handles Ctrl+C cancellation

2. **Models**: Data transfer objects and enums
    - `ProcessingMode`: Enum defining processing context (SingleImage, ImageBatch, Video)
    - `ProcessingOptions`: Encapsulates all CLI parameters (width, height, percent, etc.) and processing mode
    - `ProcessingResult`: Processing outcome with success status and error messages
    - `VideoFrame`: Video frame data with RGB24 pixel data and stride information

3. **Services**: Core business logic with interfaces for testability
    - `ImageProcessingService`: Load, process (liquid rescale), and save images with optional format override
    - `VideoProcessingService`: Extract frames from videos, convert to MagickImage, handle video trimming
    - `VideoCompilationService`: Unified video+audio encoding using FFMediaToolkit's MediaBuilder API, handles audio extraction, frame splitting, and single-pass muxing
    - `MediaProcessor`: Orchestrates parallel batch processing using `Channel<T>`, handles image-to-sequence, gradual scaling, and video output
    - `ProgressTracker`: Consolidated progress tracking and ETA calculation (supports nullable progress bar for `--no-progress` mode)
    - `DimensionInterpolator`: Calculates interpolated dimensions for gradual scaling across frames

4. **Infrastructure**: Configuration system, logging, and utilities
    - **Configuration**:
      - `ConfigurationHelper`: Builds IConfiguration from embedded defaults and user config file, manages log directory
      - `FFmpegOptions`: FFmpeg library path configuration
      - `ProcessingSettings`: Thread counts, timeouts, default values (with validation attributes)
      - `VideoEncodingOptions`: Video encoding settings (CRF, preset, codec)
      - `OutputOptions`: Output formatting preferences (suffix, progress char)
    - **Logging**:
      - `IProgressBarContext`: Interface for tracking active progress bar
      - `ProgressBarContext`: Singleton that holds reference to active progress bar for logging coordination
      - `ProgressBarAwareConsoleLogger`: Custom logger that routes output through progress bar when active
      - `ProgressBarAwareConsoleLoggerProvider`: Provider that creates progress-bar-aware loggers
      - `FileLoggerProvider`: Custom file logger with daily rotation and 7-day retention
    - **Constants**: Immutable values (file extensions, codec compatibility lists)
    - **FFmpegConfiguration**: FFmpeg path detection with result caching

5. **Handlers**: Request handling
    - `CommandHandler`: Validates CLI arguments, detects processing mode, determines output paths, collects input files, orchestrates processing

6. **Utilities**: Helper classes
    - `SharedCounter`: Thread-safe counter for concurrent progress tracking
    - `FrameOrderingBuffer`: Thread-safe buffer for maintaining frame order during parallel processing

### Processing Pipeline

**Mode Detection (CommandHandler):**

1. Analyze input path to determine processing mode:
    - Single image file → `ProcessingMode.SingleImage`
    - Single video file → `ProcessingMode.Video`
    - Directory → `ProcessingMode.ImageBatch`
2. Validate video output requirements:
    - Video output (.mp4/.mkv) only allowed for video files or image sequences
    - Ensures output extension is supported (.mp4 or .mkv)
3. Set appropriate default output path:
    - SingleImage: Same directory, filename with `-cas` suffix (e.g., `image-cas.png`)
    - ImageBatch: New folder with `-cas` suffix (e.g., `folder-cas/`)
    - Video: New folder with `-cas` suffix (e.g., `video-cas/`)
    - Video output: User-specified path with .mp4/.mkv extension

**Image Processing:**

1. Load image with ImageMagick
2. Check for image-to-sequence mode (single image + duration specified)
3. If image-to-sequence:
    - Calculate total frames: `duration × fps`
    - Check if video output is requested (output path ends with .mp4/.mkv)
    - If video output:
        - Start video encoder using FFMediaToolkit's MediaBuilder API
        - Process frames in parallel and submit to encoder via `FrameOrderingBuffer`
        - Encoder handles H.264 encoding with CRF 23
        - No audio for image-to-video conversion
    - If frame output (default):
        - Create output directory
        - For each frame, calculate interpolated dimensions (if gradual scaling enabled)
        - Apply liquid rescale to calculated dimensions
        - If gradual scaling: scale back to original dimensions using regular resize
        - Save frame with format override if specified
    - Update progress bar in real-time
4. If single image (no sequence):
    - Calculate target dimensions (from width/height or percent)
    - Apply liquid rescale (with timeout and regular resize fallback)
    - Save based on processing mode:
        - SingleImage: Save to output path directly (full file path)
        - ImageBatch: Save to output folder preserving original filename
    - Apply format override if specified

**Video Processing:**

1. Initialize FFmpeg libraries (auto-detect or use FFMPEG_PATH env var)
2. Calculate frame range if trimming requested:
    - If `--start` and `--end`: extract frames between start and end times
    - If `--start` and `--duration`: extract frames for specified duration from start
    - Otherwise: extract all frames
3. Extract frames as RGB24 using FFMediaToolkit (with optional frame range)
4. Handle stride/padding in video frame buffers:
    - FFMediaToolkit may use preallocated buffers larger than needed (e.g., 8MB)
    - Extract clean RGB24 data using reported stride from FFMediaToolkit
    - Remove row padding when stride > rowWidth
5. Convert frames to MagickImage using `ReadPixels` with `PixelReadSettings`
6. Check if video output is requested (output path ends with .mp4/.mkv):
    - **Video Output Mode:**
        - Extract audio frames from source video using FFMediaToolkit's audio stream APIs
        - Apply same trimming parameters to audio as frames
        - Split audio frames to match AAC's 1024-sample frame size requirement
        - Start unified FFMediaToolkit encoder using `MediaBuilder` API
        - Configure H.264 video encoder and AAC audio encoder
        - Process frames in parallel (max 8 threads)
        - For each frame: calculate dimensions, liquid rescale, optionally scale back
        - Submit frames to encoder via `FrameOrderingBuffer` (maintains order despite parallel processing)
        - Audio frames are synchronized and muxed during video encoding (single-pass)
        - Wait for encoding to complete (no separate merge step needed)
    - **Frame Output Mode (default):**
        - For each frame (processed in parallel, max 8 threads):
            - Calculate interpolated dimensions if gradual scaling enabled
            - Apply liquid rescale to calculated dimensions
            - If gradual scaling: scale back to original dimensions using regular resize
            - Save frame with format override (default: PNG)
        - Save frames with simple sequential naming: `frame-0001.png`, `frame-0002.png`, etc.
7. Progress bar updates in real-time with "Processing frames" message and accurate ETA based on throughput

### Concurrency Model

Uses modern async/await with producer-consumer pattern:

- `Channel<T>` for work queue
- `SemaphoreSlim` for concurrency control
- Default 16 threads for images, 8 for video frames
- Manual duration estimation based on throughput (updates dynamically after 3+ items processed)
- Real-time progress tracking with ShellProgressBar:
  - For batch images: Shows "Processing images" with current file name
  - For video frames: Shows "Processing frames" with current frame number
  - For image sequences: Shows "Generating frames" with current frame number
  - All modes calculate ETA based on actual processing speed

### Dependencies

- **ImageMagick.NET (Magick.NET-Q16-AnyCPU)**: Content-aware liquid rescaling
- **FFMediaToolkit**: Video decoding, encoding, and frame extraction
- **FFmpeg 7.x shared libraries**: Runtime dependency for FFMediaToolkit
- **System.CommandLine**: CLI argument parsing
- **ShellProgressBar**: Progress visualization with ETA
- **Microsoft.Extensions.DependencyInjection**: Dependency injection container
- **Microsoft.Extensions.Logging**: Structured logging framework
- **Microsoft.Extensions.Logging.Console**: Console logging provider with custom formatters

### FFmpeg Requirements

Video processing requires **FFmpeg 7.x shared libraries**. The application searches for FFmpeg in the following order:

1. **FFMPEG_PATH environment variable** (highest priority)
2. **Common installation paths**:
    - **macOS/Linux**: `/opt/homebrew/opt/ffmpeg@7/lib`, `/opt/homebrew/lib`, `/usr/local/lib`, `/usr/lib`
    - **Windows**: `C:\Program Files\ffmpeg\lib`, `C:\Program Files (x86)\ffmpeg\lib`

The app verifies presence of essential libraries (`libavcodec`, `libavformat` on Unix; `avcodec.dll`, `avformat.dll` on Windows).

**Windows Setup:**
FFMediaToolkit expects FFmpeg DLLs in `bin\Debug\net9.0\runtimes\win-x64\native\`. You can either:

- Place FFmpeg DLLs directly in that directory, or
- Set `FFMPEG_PATH` environment variable pointing to the directory containing FFmpeg DLLs

**Recommended:** Download FFmpeg 7.x shared build from [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds/releases)

### FFmpeg for Video Compilation

Video output features (.mp4/.mkv) use **FFMediaToolkit's MediaBuilder API** for unified video and audio encoding. All encoding is handled through FFMediaToolkit without subprocess calls:

- **H.264 video encoding:** Uses `VideoEncoderSettings` with H.264 codec
- **AAC audio encoding:** Uses `AudioEncoderSettings` with AAC codec
- **Single-pass muxing:** Audio and video are encoded and multiplexed in a single operation
- **Audio frame splitting:** Source audio frames are automatically split to match AAC's 1024-sample frame size requirement

The FFmpeg libraries are already required for FFMediaToolkit, so no additional installation is needed for video compilation.

### Error Handling

- Per-file error tracking in `ProcessingResult.ErrorMessage`
- Graceful degradation: liquid rescale timeout falls back to regular resize
- Video processing continues on frame failures, reports success if any frames succeed
- Cancellation support throughout with `CancellationToken`

### Technical Implementation Details

**Output Path Logic:**

- `CommandHandler` detects processing mode and sets output path **before** processing begins
- Three distinct behaviors based on `ProcessingMode`:
  - **SingleImage**: OutputPath is a full file path (e.g., `/path/image-cas.png`)
  - **ImageBatch**: OutputPath is a folder; files retain original names
  - **Video**: OutputPath is a folder; frames use sequential naming
- `MediaProcessor` adapts behavior based on the mode set by `CommandHandler`
- Ensures `-cas` suffix appears exactly once in the output path hierarchy

**Video Frame Buffer Handling:**

- FFMediaToolkit returns video frames in preallocated buffers (often 8MB for memory alignment)
- Actual RGB24 data size: `width × height × 3 bytes`
- Buffer may contain row padding (stride > rowWidth)
- Implementation uses `ExtractCleanRGB24Data` to remove padding row-by-row
- Stride value from FFMediaToolkit's `ImageData.Stride` property is used for accurate extraction

**ImageMagick Pixel Import:**

- Direct byte array construction (`new MagickImage(byte[], MagickReadSettings)`) unreliable for raw pixel data
- Current implementation uses `ReadPixels` method with `PixelReadSettings`:
  - Creates blank MagickImage with target dimensions
  - Imports RGB data using `StorageType.Char` and `PixelMapping.RGB`
  - Provides reliable conversion from raw RGB24 byte arrays

**Gradual Scaling Implementation:**

- **Detection**: `IsGradualScaling` property compares start dimensions with target dimensions
- **Dimension Calculation**: `DimensionInterpolator` service calculates dimensions for each frame:
  - Uses linear interpolation: `dimension[i] = start + (target - start) × (i / (total_frames - 1))`
  - Handles both absolute dimensions (width/height) and percentages
  - Defaults: start = 100% (original), target = 50%
- **Scale-Back Feature**: When gradual scaling is enabled:
  - Each frame is first liquid rescaled to its interpolated dimensions
  - Then scaled back to original dimensions using regular resize (not liquid rescale)
  - Ensures all output frames have identical dimensions for video concatenation
  - Example: Original 1920×1080 video with `-sp 100 -p 50`:
    - Frame 1: liquid rescale to 1920×1080 (100%) → resize to 1920×1080 (no change)
    - Frame 50: liquid rescale to 1440×810 (75%) → resize back to 1920×1080
    - Frame 100: liquid rescale to 960×540 (50%) → resize back to 1920×1080
    - Result: All frames 1920×1080, ready for ffmpeg concatenation
- **When Scale-Back Applies**: Only when gradual scaling is active
  - Video/sequence with gradual scaling → scale back to original ✓
  - Video/sequence without gradual scaling → keep at target dimensions (user's intent)
  - Single image (no sequence) → no scale-back needed

**Output Format Override:**

- Default formats:
  - Single/batch images: preserve input format
  - Video frames: PNG (was JPG in earlier versions)
  - Image sequences: PNG
  - Video output: H.264/MP4 or MKV (based on audio codec)
- User can override with `--format` option for frame-based output modes
- `--format` option is ignored when output is .mp4/.mkv (video encoding uses H.264)
- Format applied during save operation, with extension automatically updated

**Frame Ordering During Parallel Processing:**

- `FrameOrderingBuffer` utility maintains correct frame sequence during parallel processing
- Accepts frames in any order (as they complete processing)
- Releases frames sequentially to encoder/disk to maintain temporal order
- Thread-safe buffer prevents race conditions during concurrent frame submission
- Essential for video encoding where frame order must be preserved

**Audio Frame Splitting for AAC Encoding:**

- AAC encoder requires exactly 1024 samples per frame
- Source audio may have different frame sizes (commonly 2048 samples)
- `SplitAudioFramesToAacFrameSize()` splits source frames into 1024-sample chunks
- Timestamps are recalculated based on total samples processed to ensure perfect chronological ordering
- Example: 388 source frames × 2048 samples = 776 AAC frames × 1024 samples
- Prevents "nb_samples > frame_size" errors and ensures correct audio speed/synchronization

## Development Notes

### Recent Improvements

**VideoCompilationService Refactoring - Completed:**

The `VideoCompilationService` has been successfully refactored to use **FFMediaToolkit's MediaBuilder API** instead of subprocess calls:

- **Previous approach:** Three-stage process with FFmpeg subprocess calls (extract audio → encode video → merge)
- **Current approach:** Single-pass unified encoding using FFMediaToolkit
  - Uses `MediaBuilder.CreateContainer()` with `.WithVideo()` and `.WithAudio()` fluent API
  - Audio extraction via FFMediaToolkit's `MediaFile.Open()` with audio-only mode
  - Audio frame splitting to handle AAC's 1024-sample frame size requirement
  - Direct `AddFrame()` calls for both video and audio streams
  - Automatic audio/video synchronization during encoding

**Benefits achieved:**

- Eliminated all subprocess calls - everything uses FFMediaToolkit
- Simplified architecture with single-pass encoding
- Improved maintainability with consistent API usage throughout codebase
- Better error handling without subprocess management complexity
- Reduced overhead by eliminating process creation and IPC

**Location:** [Services/VideoCompilationService.cs](Services/VideoCompilationService.cs)

### Feature Status

**Video Processing** - Fully functional using FFMediaToolkit:

- **Frame Extraction**: Uses FFMediaToolkit's `MediaFile` and `VideoStream` APIs to extract frames
- **Video Trimming**: Supports start/end time or duration-based trimming
- **Pixel Format**: Configured for RGB24 output for compatibility with ImageMagick
- **Buffer Handling**: Implements stride-aware padding removal for preallocated buffers
- **ImageMagick Import**: Uses `ReadPixels` with `PixelReadSettings` for reliable raw pixel data import
- **Progress Tracking**: Real-time progress bar with dynamic ETA calculation based on frame processing throughput (can be disabled with `--no-progress`)
- **Parallel Processing**: Processes up to 8 frames concurrently with proper progress tracking for each frame

**Video Compilation** - Fully functional using FFMediaToolkit:

- **Unified Encoding**: Single-pass video+audio encoding using `MediaBuilder` API
- **H.264 Video**: High-quality encoding with configurable CRF settings
- **AAC Audio**: Automatic audio extraction, frame splitting, and synchronization
- **No Subprocesses**: All encoding handled through FFMediaToolkit without external process calls
- **Audio Trimming**: Synchronized audio trimming when using `--start`/`--end`/`--duration`

**Image-to-Sequence** - Fully functional:

- Generates frame sequences from single images with configurable FPS
- Supports gradual scaling across the sequence
- Real-time progress tracking with ETA
- Sequential frame naming for easy video compilation

**Gradual Scaling** - Fully functional:

- Works with both videos and image sequences
- Linear interpolation between start and end dimensions
- Scale-back feature ensures uniform frame sizes for video workflows
- Automatic detection when start dimensions differ from target dimensions

## Use Cases & Workflows

### Single Image Processing

**Use case**: Resize a single image with content-aware scaling

```bash
dotnet run -- photo.jpg -p 75
```

Output: Single image at 75% of original size

### Batch Image Processing

**Use case**: Process multiple images in a folder

```bash
dotnet run -- ./photos/ -p 50 -t 16
```

Output: All images scaled to 50%, preserving filenames

### Image-to-Sequence (Static Dimensions)

**Use case**: Create video frames from a single image

```bash
dotnet run -- poster.jpg --duration 5 -w 1920 -h 1080
```

Output: 125 frames (5s × 25fps) all at 1920×1080

### Image-to-Sequence (Gradual Scaling)

**Use case**: Create a "zoom out" effect from an image

```bash
dotnet run -- logo.jpg --duration 3 -sp 100 -p 50 --fps 30
```

Output: 90 frames gradually scaling from 100% to 50%, all uniform size

### Video Trimming

**Use case**: Extract a specific portion of a video

```bash
dotnet run -- input.mp4 --start 30 --end 60 -p 50
```

Output: Frames from 30s to 60s, scaled to 50%

### Video with Gradual Scaling

**Use case**: Create a smooth zoom effect across entire video

```bash
dotnet run -- video.mp4 -sp 100 -p 50 -f png
```

Output: All video frames with gradual scaling from 100% to 50%, uniform dimensions for ffmpeg

### Video Trimming + Gradual Scaling

**Use case**: Extract clip with zoom effect

```bash
dotnet run -- video.mp4 --start 10 --duration 5 -sp 100 -p 50
```

Output: 5-second clip with gradual scaling, ready for video compilation

### Video to Video with Audio

**Use case**: Process a video and output directly to video file with audio

```bash
dotnet run -- input.mp4 -o output.mp4 -p 75
```

Output: Single MP4 file at 75% size with preserved audio

### Image Sequence to Video

**Use case**: Create a video file from a single image with zoom effect

```bash
dotnet run -- poster.jpg --duration 5 -o output.mp4 -sp 100 -p 50
```

Output: 5-second MP4 video with gradual zoom effect (no audio)

## Supported Formats

**Images:** `.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.tif`, `.webp`, `.ico`

**Videos (Input):** `.mp4`, `.avi`, `.mov`, `.mkv`, `.webm`, `.wmv`, `.flv`, `.m4v`

**Videos (Output):** `.mp4`, `.mkv`

**Frame Output Formats (via --format):** `png`, `jpg`, `bmp`, `tiff`

**Audio Codecs:** AAC, MP3, AC3, EAC3, MP2 (MP4 compatible); all codecs supported in MKV container
