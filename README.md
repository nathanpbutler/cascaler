# cascaler

A .NET CLI tool for content-aware scaling (seam carving / liquid rescaling) of images and videos.

<p align="center">
  <a href="https://www.youtube.com/watch?v=dQw4w9WgXcQ"><img src="Assets/rick.gif" alt="Rick Astley's face content-aware scaled"></a>
  <br>
    <em>Click to watch demo video</em>
</p>

## Features

- Content-aware scaling (a.k.a seam carving or liquid rescaling) of images and videos
- Process individual images or entire directories in parallel
- Extract frames from videos, apply scaling, and output as frame sequences or video files
- Audio effects: vibrato and tremolo filters for creative audio manipulation
- Gradual scaling over image sequences or video duration
- Customizable scaling parameters (width, height, percentage)
- Supports common image formats (JPEG, PNG, BMP, TIFF, GIF, WebP) and video formats (MP4, AVI, MOV, MKV)
- Command-line interface with detailed options
- Progress bar with estimated time remaining

## Requirements

- .NET 10.0 or higher
- FFmpeg 7.x shared libraries (for video processing)

### FFmpeg Installation

**macOS (Homebrew):**

```bash
brew install ffmpeg@7
```

**Linux (Ubuntu/Debian):**

```bash
sudo apt update
sudo apt install ffmpeg
```

**Windows:**

Download a shared build from [https://www.gyan.dev/ffmpeg/builds](https://www.gyan.dev/ffmpeg/builds) and either:

- Add FFmpeg `bin` directory to your system `PATH`, or
- Set `FFMPEG_PATH` environment variable pointing to the directory containing FFmpeg DLLs, or
- Extract DLLs to `bin\Debug\net9.0\runtimes\win-x64\native\`

## Installation

```bash
git clone https://github.com/nathanpbutler/cascaler.git
cd cascaler
dotnet build
```

## Usage

### Basic Image Rescaling

Apply content-aware scaling to individual images.

```bash
# Scale single image to 50% (default)
cascaler input.jpg

# Scale to specific dimensions
cascaler input.jpg -w 800 -h 600

# Scale to percentage with custom thread count
cascaler input.jpg -p 75 -t 8
```

### Batch Processing

Process multiple images from a directory in parallel.

```bash
# Process entire folder
cascaler /path/to/images -p 50

# Process with custom dimensions
cascaler /path/to/images -w 1920 -h 1080
```

### Video Processing

Extract frames from video files, apply liquid rescaling, and output as frame sequences or video files.

```bash
# Extract and process all video frames
cascaler input.mp4

# Process video with output to MP4 (preserves audio)
cascaler input.mp4 -o output.mp4 -p 75

# Process video with vibrato and tremolo audio effects
cascaler input.mp4 -o output.mp4 -p 75 --vibrato

# Trim video and process
cascaler input.mp4 --start 10 --duration 5 -p 50
```

### Gradual Scaling

Increase or decrease the liquid rescaling intensity over the duration of the image sequence or video.

```bash
# Image to image sequence with gradual scaling
cascaler input.jpg --duration 3 -sp 100 -p 50

# Video to video with gradual scaling
cascaler input.mp4 -o output.mp4 -sp 100 -p 50

# Trim a specific segment of a video to video with gradual scaling
cascaler input.mp4 --start 10 --duration 5 -sp 100 -p 50
```

### Image-to-Video Sequences

Generate frame sequences or video files from static images at a specified frame rate or duration.

```bash
# Generate 30fps sequence from image
cascaler input.jpg --duration 2 --fps 30 -w 1920 -h 1080

# Output directly to video file
cascaler input.jpg --duration 5 -o output.mp4 -sp 100 -p 50
```

## Command-Line Options

### Scaling Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--width` | `-w` | Target width in pixels | - |
| `--height` | `-h` | Target height in pixels | - |
| `--percent` | `-p` | Scale percentage | 50 |
| `--start-percent` | `-sp` | Starting percentage for gradual scaling | 100 |
| `--start-width` | `-sw` | Starting width for gradual scaling | - |
| `--start-height` | `-sh` | Starting height for gradual scaling | - |

### Advanced Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--deltaX` | `-d` | Seam transversal step (0=straight, 1=curved) | 1.0 |
| `--rigidity` | `-r` | Bias for non-straight seams | 1.0 |
| `--threads` | `-t` | Parallel processing threads | 16 |
| `--output` | `-o` | Output path | input + `-cas` suffix |
| `--no-progress` | - | Disable progress bar and progress updates | false |

### Video & Sequence Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--format` | `-f` | Output image format (png, jpg, bmp, tiff) | input format |
| `--start` | - | Start time in seconds for video trimming | - |
| `--end` | - | End time in seconds for video trimming | - |
| `--duration` | - | Duration in seconds | - |
| `--fps` | - | Frame rate for sequences | 25 |
| `--vibrato` | - | Apply vibrato and tremolo audio effects | false |

## Configuration

cascaler supports persistent configuration to customize defaults without specifying command-line arguments every time.

### Configuration Management Commands

Manage your configuration using the `config` subcommand:

```bash
# Show current effective configuration
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

### Configuration File Location

The application reads configuration from a user-specific config file:

- **Unix/macOS/Linux**: `~/.config/cascaler/appsettings.json`
- **Windows**: `%APPDATA%\cascaler\appsettings.json`

### Configuration Priority

Settings are applied in the following order (later overrides earlier):

1. Embedded defaults (built into the executable)
2. User configuration file
3. Command-line arguments (highest priority)

### Example Configuration

Create the config file with customized settings:

```json
{
  "FFmpeg": {
    "LibraryPath": "/opt/homebrew/opt/ffmpeg@7/lib"
  },
  "Processing": {
    "MaxImageThreads": 32,
    "DefaultScalePercent": 75,
    "DefaultFps": 30
  },
  "VideoEncoding": {
    "DefaultCRF": 20
  },
  "Output": {
    "Suffix": "-scaled"
  }
}
```

### Available Settings

#### FFmpeg Section

- `LibraryPath`: Path to FFmpeg libraries (empty for auto-detection)
  - Use `cascaler config init --detect-ffmpeg` to automatically populate this
  - If specified path doesn't exist, falls back to auto-detection (if enabled)
- `EnableAutoDetection`: Enable automatic FFmpeg detection (default: true)
  - When `true`: If `LibraryPath` is invalid/empty, automatically searches for FFmpeg
  - When `false`: Only uses `LibraryPath`, fails if invalid
  - **Recommended:** Keep as `true` for fallback behavior across different machines

#### Processing Section

- `MaxImageThreads`: Parallel threads for image processing (default: 16)
- `MaxVideoThreads`: Parallel threads for video processing (default: 8)
- `ProcessingTimeoutSeconds`: Timeout for liquid rescale operations (default: 30)
- `DefaultScalePercent`: Default scaling percentage (default: 50)
- `DefaultFps`: Default FPS for sequences (default: 25)
- `DefaultVideoFrameFormat`: Default format for video frames (default: "png")

#### VideoEncoding Section

- `DefaultCRF`: H.264 quality, 0-51, lower = better (default: 23)
- `DefaultPreset`: Encoding speed preset (default: "medium")
- `DefaultPixelFormat`: Pixel format for compatibility (default: "yuv420p")
- `DefaultCodec`: Video codec (default: "libx264")

#### Output Section

- `Suffix`: Output file/folder suffix (default: "-cas")
- `ProgressCharacter`: Character for progress bar (default: "─")
- `ShowEstimatedDuration`: Show ETA in progress bar (default: true)

### Performance Tip

Configuring `FFmpeg.LibraryPath` eliminates the need for runtime FFmpeg detection on every execution, significantly improving startup time.

## Output Formats

**Supported Input Images:** `.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.tif`, `.webp`, `.ico`

**Supported Input Videos:** `.mp4`, `.avi`, `.mov`, `.mkv`, `.webm`, `.wmv`, `.flv`, `.m4v`

**Video Output:** `.mp4`, `.mkv` (H.264 encoding with audio preservation)

**Frame Output:** `png`, `jpg`, `bmp`, `tiff` (via `--format`)

## Architecture

Built on .NET 10.0 with dependency injection and async/await patterns:

- **ImageMagick.NET** - Content-aware liquid rescaling
- **FFmpeg.AutoGen 7.1.1** - Direct P/Invoke bindings to FFmpeg for video/audio processing and filtering
- **System.CommandLine** - CLI argument parsing
- **ShellProgressBar** - Progress visualization with ETA (integrated with logging system)
- **Microsoft.Extensions.Logging** - Structured logging with progress-bar-aware console output

Processing uses a producer-consumer pattern with `Channel<T>` for efficient parallel processing. Default of 16 threads for images, 8 for video frames.

**Video Processing:** Uses native FFmpeg libraries (libavcodec, libavformat, libavutil, libswscale, libswresample, libavfilter) via FFmpeg.AutoGen for:
- Video decoding and encoding (H.264/H.265)
- Audio decoding and encoding (AAC-LC)
- Audio filtering (vibrato, tremolo effects)
- Container muxing (MP4/MKV)

**Logging:** Uses a custom progress-bar-aware logging system that coordinates console output with ShellProgressBar to prevent visual conflicts. When `--no-progress` is used, only important log messages are displayed without progress updates.

## License

MIT License. See [LICENSE](LICENSE) for details.
