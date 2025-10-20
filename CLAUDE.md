# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**cascaler** is a high-performance batch liquid rescaling tool for images and videos using content-aware seam carving (liquid rescaling). Built with .NET 9.0, it processes media files in parallel using ImageMagick for liquid rescaling and FFMediaToolkit for video frame extraction.

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
```

## Command-Line Options

- `--width` / `-w`: Target width in pixels
- `--height` / `-h`: Target height in pixels
- `--percent` / `-p`: Scale percentage (default: 50)
- `--deltaX` / `-d`: Seam transversal step (0=straight, 1=curved, default: 1.0)
- `--rigidity` / `-r`: Bias for non-straight seams (default: 1.0)
- `--threads` / `-t`: Parallel processing threads (default: 16)
- `--output` / `-o`: Output folder (default: input + "-cas")

Note: Cannot specify both width/height and percent - choose one scaling method.

## Architecture

### Project Structure

The application uses a clean, layered architecture with dependency injection for maintainability and testability:

```plaintext
cascaler/
├── Program.cs                          # Entry point with DI setup (~160 lines)
├── Models/
│   ├── ProcessingOptions.cs           # CLI argument encapsulation
│   ├── ProcessingResult.cs            # Processing outcome data
│   └── VideoFrame.cs                  # Video frame metadata
├── Services/
│   ├── Interfaces/
│   │   ├── IImageProcessingService.cs
│   │   ├── IVideoProcessingService.cs
│   │   ├── IMediaProcessor.cs
│   │   └── IProgressTracker.cs
│   ├── ImageProcessingService.cs      # ImageMagick operations
│   ├── VideoProcessingService.cs      # FFMediaToolkit integration
│   ├── MediaProcessor.cs              # Batch processing orchestration
│   └── ProgressTracker.cs             # Centralized ETA calculation
├── Infrastructure/
│   ├── Constants.cs                   # File extensions, defaults, magic numbers
│   ├── ProcessingConfiguration.cs     # App-wide configuration
│   └── FFmpegConfiguration.cs         # FFmpeg path detection
├── Handlers/
│   └── CommandHandler.cs              # CLI command orchestration
└── Utilities/
    └── SharedCounter.cs               # Thread-safe counter

```

### Key Components

1. **Program.cs**: Minimal entry point (~160 lines)
   - Dependency injection container setup
   - Command-line interface configuration with System.CommandLine
   - Ctrl+C cancellation handling

2. **Models**: Data transfer objects
   - `ProcessingOptions`: Encapsulates all CLI parameters (width, height, percent, etc.)
   - `ProcessingResult`: Processing outcome with success status and error messages
   - `VideoFrame`: Video frame data with RGB24 pixel data and stride information

3. **Services**: Core business logic with interfaces for testability
   - `ImageProcessingService`: Load, process (liquid rescale), and save images
   - `VideoProcessingService`: Extract frames from videos, convert to MagickImage
   - `MediaProcessor`: Orchestrates parallel batch processing using `Channel<T>`
   - `ProgressTracker`: Consolidated progress tracking and ETA calculation

4. **Infrastructure**: Configuration and utilities
   - `Constants`: Centralized magic numbers, file extensions, defaults
   - `ProcessingConfiguration`: Runtime configuration (thread counts, timeouts)
   - `FFmpegConfiguration`: Automatic FFmpeg library detection

5. **Handlers**: Request handling
   - `CommandHandler`: Validates CLI arguments, collects input files, orchestrates processing

6. **Utilities**: Helper classes
   - `SharedCounter`: Thread-safe counter for concurrent progress tracking

### Processing Pipeline

**Image Processing:**

1. Load image with ImageMagick
2. Calculate target dimensions (from width/height or percent)
3. Apply liquid rescale (with timeout and regular resize fallback)
4. Save to output folder with "-cas" suffix

**Video Processing:**

1. Initialize FFmpeg libraries (auto-detect or use FFMPEG_PATH env var)
2. Extract all frames as RGB24 using FFMediaToolkit
3. Handle stride/padding in video frame buffers:
   - FFMediaToolkit may use preallocated buffers larger than needed (e.g., 8MB)
   - Extract clean RGB24 data using reported stride from FFMediaToolkit
   - Remove row padding when stride > rowWidth
4. Convert frames to MagickImage using `ReadPixels` with `PixelReadSettings`
5. Process each frame through image pipeline in parallel (max 8 threads)
6. Progress bar updates in real-time with "Processing frames" message and accurate ETA based on throughput
7. Save frames to subfolder: `{videoname}-cas/frame-NNNN-cas.jpg`

### Concurrency Model

Uses modern async/await with producer-consumer pattern:

- `Channel<T>` for work queue
- `SemaphoreSlim` for concurrency control
- Default 16 threads for images, 8 for video frames
- Manual duration estimation based on throughput (updates dynamically after 3+ items processed)
- Real-time progress tracking with ShellProgressBar:
  - For images: Shows "Processing images" with current file name
  - For video frames: Shows "Processing frames" with current frame number
  - Both modes calculate ETA based on actual processing speed

### Dependencies

- **ImageMagick.NET (Magick.NET-Q16-AnyCPU)**: Content-aware liquid rescaling
- **FFMediaToolkit**: Video decoding and frame extraction
- **System.CommandLine**: Modern CLI argument parsing
- **ShellProgressBar**: Progress visualization with ETA
- **Microsoft.Extensions.DependencyInjection**: Dependency injection container

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

### Error Handling

- Per-file error tracking in `ProcessingResult.ErrorMessage`
- Graceful degradation: liquid rescale timeout falls back to regular resize
- Video processing continues on frame failures, reports success if any frames succeed
- Cancellation support throughout with `CancellationToken`

### Technical Implementation Details

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

## Development Notes

### Video Processing Status

Video processing is **fully functional** using FFMediaToolkit. Key implementation details:

- **Frame Extraction**: Uses FFMediaToolkit's `MediaFile` and `VideoStream` APIs to extract all frames
- **Pixel Format**: Configured for RGB24 output for compatibility with ImageMagick
- **Buffer Handling**: Implements stride-aware padding removal for preallocated buffers
- **ImageMagick Import**: Uses `ReadPixels` with `PixelReadSettings` for reliable raw pixel data import
- **Progress Tracking**: Real-time progress bar with dynamic ETA calculation based on frame processing throughput
- **Parallel Processing**: Processes up to 8 frames concurrently with proper progress tracking for each frame

## Supported Formats

**Images:** `.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.tif`, `.webp`, `.ico`

**Videos:** `.mp4`, `.avi`, `.mov`, `.mkv`, `.webm`, `.wmv`, `.flv`, `.m4v`
