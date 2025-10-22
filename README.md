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
- Gradual scaling over image sequences or video duration
- Customizable scaling parameters (width, height, percentage)
- Supports common image formats (JPEG, PNG, BMP, TIFF, GIF, WebP) and video formats (MP4, AVI, MOV, MKV)
- Command-line interface with detailed options
- Progress bar with estimated time remaining

## Requirements

- .NET 9.0 or higher
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
dotnet run -- input.jpg

# Scale to specific dimensions
dotnet run -- input.jpg -w 800 -h 600

# Scale to percentage with custom thread count
dotnet run -- input.jpg -p 75 -t 8
```

### Batch Processing

Process multiple images from a directory in parallel.

```bash
# Process entire folder
dotnet run -- /path/to/images -p 50

# Process with custom dimensions
dotnet run -- /path/to/images -w 1920 -h 1080
```

### Video Processing

Extract frames from video files, apply liquid rescaling, and output as frame sequences or video files.

```bash
# Extract and process all video frames
dotnet run -- input.mp4

# Process video with output to MP4 (preserves audio)
dotnet run -- input.mp4 -o output.mp4 -p 75

# Trim video and process
dotnet run -- input.mp4 --start 10 --duration 5 -p 50
```

### Gradual Scaling

Increase or decrease the liquid rescaling intensity over the duration of the image sequence or video.

```bash
# Image to image sequence with gradual scaling
dotnet run -- input.jpg --duration 3 -sp 100 -p 50

# Video to video with gradual scaling
dotnet run -- input.mp4 -o output.mp4 -sp 100 -p 50

# Trim a specific segment of a video to video with gradual scaling
dotnet run -- input.mp4 --start 10 --duration 5 -sp 100 -p 50
```

### Image-to-Video Sequences

Generate frame sequences or video files from static images at a specified frame rate or duration.

```bash
# Generate 30fps sequence from image
dotnet run -- input.jpg --duration 2 --fps 30 -w 1920 -h 1080

# Output directly to video file
dotnet run -- input.jpg --duration 5 -o output.mp4 -sp 100 -p 50
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
| `--no-progress` | - | Disable progress bar | false |

### Video & Sequence Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--format` | `-f` | Output image format (png, jpg, bmp, tiff) | input format |
| `--start` | - | Start time in seconds for video trimming | - |
| `--end` | - | End time in seconds for video trimming | - |
| `--duration` | - | Duration in seconds | - |
| `--fps` | - | Frame rate for sequences | 25 |

## Output Formats

**Supported Input Images:** `.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.tif`, `.webp`, `.ico`

**Supported Input Videos:** `.mp4`, `.avi`, `.mov`, `.mkv`, `.webm`, `.wmv`, `.flv`, `.m4v`

**Video Output:** `.mp4`, `.mkv` (H.264 encoding with audio preservation)

**Frame Output:** `png`, `jpg`, `bmp`, `tiff` (via `--format`)

## Architecture

Built on .NET 9.0 with dependency injection and async/await patterns:

- **ImageMagick.NET** - used for liquid rescaling
- **FFMediaToolkit** - Video decoding, encoding, and frame extraction
- **System.CommandLine** - CLI argument parsing
- **ShellProgressBar** - Progress visualization with ETA

Processing uses a producer-consumer pattern with `Channel<T>` for efficient parallel processing. Default of 16 threads for images, 8 for video frames.

## License

MIT License. See [LICENSE](LICENSE) for details.
