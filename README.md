# cascaler

**Create Content Aware Scale videos in minutes, not hours.**

A high-performance CLI tool for Content Aware Scaling (a.k.a liquid rescaling / seam carving) of images and videos. Built to make Content Aware Scale meme videos actually practical to create. What used to take hours in Photoshop now takes minutes with parallel processing and native video support.

[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download)
[![FFmpeg](https://img.shields.io/badge/FFmpeg-7.x-orange.svg)](https://ffmpeg.org/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

<div align="center">
  <a href="https://www.youtube.com/watch?v=dQw4w9WgXcQ"><img src="Assets/rick.gif" alt="Rick Astley's face content-aware scaled"></a>
  <br>
  <em>Click to watch demo video</em>
</div>

## The Story

Ever seen those stupid Content Aware Scale videos on YouTube before? I'm responsible for a few of those... this one in particular: [Content Aware Scale - "Show me your war face!"](https://www.youtube.com/watch?v=a8k3b-QNbhs). The problem? Each video took **hours** to create.

While Neil Cicierega's [Animated Content Aware Scaling script for Adobe Photoshop](https://neilblr.com/post/42948042669) helped automate the process, it was still painfully slow since Photoshop only processes one frame at a time. For longer videos, this meant waiting hours for Photoshop to finish processing each frame sequentially.

I built cascaler to solve this bottleneck. What used to take hours of waiting for Photoshop now completes in **minutes** with parallel batch processing.

## Why cascaler?

### Performance

- **Parallel processing** - Process multiple frames simultaneously
- **Native video support** - No more exporting/importing image sequences manually
- **Significantly faster** than the Photoshop script workflow

### Features

- **Gradual scaling** - Smooth\* transitions from 100% to any percentage over time
- **Audio effects** - Built-in vibrato and tremolo filters for extra weirdness *(extra customization to come)*
- **Frame-accurate trimming** - Process specific segments without re-encoding the entire video
- **Scale-back mode** - Apply the liquid rescaling effect then scale back to original dimensions
- **Audio preservation** - AAC audio encoding with proper sync *(extra codecs coming soon)*
- **Progress tracking** - Real-time progress bars with ETA
- **Persistent configuration** - Save your preferred settings

<sub>*\*As smooth as seam carving can be...*</sub>

## Quick Start

### Requirements

- .NET 10.0 or higher
- FFmpeg 7.x libraries (for video processing)

### Installation

```bash
# Clone and build
git clone https://github.com/nathanpbutler/cascaler.git
cd cascaler
dotnet build

# Install FFmpeg (if not already installed)
brew install ffmpeg@7              # macOS
sudo apt install ffmpeg            # Linux
# Windows: Download from https://www.gyan.dev/ffmpeg/builds
```

For detailed FFmpeg setup instructions, see [Configuration Guide](Documentation/Configuration.md#ffmpeg-section).

## Usage Examples

### Basic Image Processing

```bash
# Scale a single image to 50% (default)
cascaler input.jpg

# Scale to specific dimensions
cascaler input.jpg -w 800 -h 600

# Process an entire directory
cascaler /path/to/images -p 75
```

### Video Processing

```bash
# Process a video (preserves audio)
cascaler input.mp4 -o output.mp4 -p 75

# Trim and process a video segment
cascaler input.mp4 -o output.mp4 --start 10 --duration 5 -p 50

# Add audio effects
cascaler input.mp4 -o output.mp4 --vibrato -p 75
```

### Creative Effects

```bash
# Gradual scaling effect (100% → 50%)
cascaler input.mp4 -o output.mp4 -sp 100 -p 50

# Image to video with gradual scaling
cascaler input.jpg -o output.mp4 --duration 3 -sp 100 -p 50

# Apply effect and scale back to original size
cascaler input.jpg --scale-back -p 50

# Convert directory to video with gradual scaling
cascaler /path/to/images -o output.mp4 -sp 75 -p 25
```

For complete command-line reference and advanced usage, see [Command-Line Reference](Documentation/CommandLineReference.md).

## Common Options

| Option                 | Alias       | Description                                         |
|------------------------|-------------|-----------------------------------------------------|
| `--percent`            | `-p`        | Scale percentage (default: 50)                      |
| `--width` / `--height` | `-w` / `-h` | Target dimensions in pixels                         |
| `--start-percent`      | `-sp`       | Starting percentage for gradual scaling             |
| `--output`             | `-o`        | Output path (file or directory)                     |
| `--threads`            | `-t`        | Number of parallel processing threads (default: 16) |
| `--duration`           | -           | Duration in seconds (for image sequences)           |
| `--fps`                | -           | Frame rate for sequences (default: 25)              |
| `--vibrato`            | -           | Apply vibrato and tremolo audio effects             |
| `--scale-back`         | -           | Scale back to original 100% dimensions              |

For a complete list of options, see [Command-Line Reference](Documentation/CommandLineReference.md).

## Configuration

Save your preferred settings to avoid repeating command-line options:

```bash
# Initialize configuration with FFmpeg detection
cascaler config init --detect-ffmpeg

# View current configuration
cascaler config show

# Show config file location
cascaler config path
```

**Configuration file location:**

- Unix/macOS/Linux: `~/.config/cascaler/appsettings.json`
- Windows: `%APPDATA%\cascaler\appsettings.json`

**Example configuration:**

```json
{
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

Settings priority: Embedded defaults → User config → Command-line arguments

For detailed configuration options and examples, see [Configuration Guide](Documentation/Configuration.md).

## Supported Formats

**Input:**

- Images: `.jpg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.webp`, `.ico`
- Videos: `.mp4`, `.avi`, `.mov`, `.mkv`, `.webm`, `.wmv`, `.flv`, `.m4v`

**Output:**

- Videos: `.mp4`, `.mkv` (H.264 encoding with AAC audio)
- Frames: `png` (default), `jpg`, `bmp`, `tiff`

## Under the Hood

Built with modern .NET 10.0 and powered by:

- **ImageMagick.NET** for seam carving
- **FFmpeg.AutoGen 7.1.1** for native video/audio processing
- **Parallel processing** (configurable: 16 threads for images, 8 for video)

See [Architecture Documentation](Documentation/Architecture.md) for technical details.

## Documentation

- **[Command-Line Reference](Documentation/CommandLineReference.md)** - Complete guide to all command-line options and usage patterns
- **[Configuration Guide](Documentation/Configuration.md)** - Persistent configuration setup, FFmpeg path detection, and advanced settings
- **[Architecture Documentation](Documentation/Architecture.md)** - Technical overview, processing flows, and implementation details

## Contributing

Contributions are welcome! Please feel free to submit issues, feature requests, or pull requests.

## License

MIT License. See [LICENSE](LICENSE) for details.
