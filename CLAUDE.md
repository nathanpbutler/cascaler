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

### Single-File Design

The entire application is contained in `Program.cs` (~1100 lines) with three main components:

1. **Supporting Classes** (lines 12-37)
   - `SharedCounter`: Thread-safe counter for progress tracking
   - `ProcessingResult`: Encapsulates processing outcome and errors
   - `VideoFrame`: Stores extracted video frame data with metadata (includes stride information for padding removal)

2. **Program Class** (lines 39-755)
   - Main entry point with System.CommandLine for argument parsing
   - Orchestrates parallel processing using `Channel<T>` and `SemaphoreSlim`
   - Handles both image and video processing workflows
   - Progress tracking with ShellProgressBar
   - Cancellation support (Ctrl+C handling)

3. **Service Classes**
   - `ImageProcessingService` (lines 757-900): ImageMagick operations for loading, processing (liquid rescale with fallback), and saving images
   - `VideoProcessingService` (lines 903-1149): FFMediaToolkit integration for frame extraction, RGB24 conversion with stride handling, and ImageMagick pixel import

### Processing Pipeline

**Image Processing:**

1. Load image with ImageMagick
2. Calculate target dimensions (from width/height or percent)
3. Apply liquid rescale (with timeout and regular resize fallback)
4. Save to output folder with "-cas" suffix

**Video Processing:**

1. Initialize FFmpeg libraries (auto-detect or use FFMPEG_PATH env var)
2. Extract frames as RGB24 using FFMediaToolkit (currently limited to 10 frames for testing)
3. Handle stride/padding in video frame buffers:
   - FFMediaToolkit may use preallocated buffers larger than needed (e.g., 8MB)
   - Extract clean RGB24 data using reported stride from FFMediaToolkit
   - Remove row padding when stride > rowWidth
4. Convert frames to MagickImage using `ReadPixels` with `PixelReadSettings`
5. Process each frame through image pipeline in parallel (max 8 threads)
6. Save frames to subfolder: `{videoname}-cas/frame-NNNN-cas.jpg`

### Concurrency Model

Uses modern async/await with producer-consumer pattern:

- `Channel<T>` for work queue
- `SemaphoreSlim` for concurrency control
- Default 16 threads for images, 8 for video frames
- Manual duration estimation based on throughput

### Dependencies

- **ImageMagick.NET (Magick.NET-Q16-AnyCPU)**: Content-aware liquid rescaling
- **FFMediaToolkit**: Video decoding and frame extraction
- **System.CommandLine**: Modern CLI argument parsing
- **ShellProgressBar**: Progress visualization with ETA

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

- **Frame Extraction**: Uses FFMediaToolkit's `MediaFile` and `VideoStream` APIs
- **Pixel Format**: Configured for RGB24 output for compatibility with ImageMagick
- **Buffer Handling**: Implements stride-aware padding removal for preallocated buffers
- **ImageMagick Import**: Uses `ReadPixels` with `PixelReadSettings` for reliable raw pixel data import
- **Frame Limit**: Currently hardcoded to 10 frames for testing purposes

## Supported Formats

**Images:** `.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.tif`, `.webp`, `.ico`

**Videos:** `.mp4`, `.avi`, `.mov`, `.mkv`, `.webm`, `.wmv`, `.flv`, `.m4v`
