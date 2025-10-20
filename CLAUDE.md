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

# Example: Gradual scaling on image (generates sequence from 100% to 50% over 3 seconds)
dotnet run -- input.jpg --duration 3 -sp 100 -p 50

# Example: Gradual scaling on video with format override
dotnet run -- input.mp4 -sp 100 -p 50 -f jpg

# Example: Trim video and apply gradual scaling
dotnet run -- input.mp4 --start 10 --duration 5 -sp 100 -p 50

# Example: Generate 30fps image sequence with custom dimensions
dotnet run -- input.jpg --duration 2 --fps 30 -w 1920 -h 1080
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

### Gradual Scaling Options
- `--start-width` / `-sw`: Start width for gradual scaling
- `--start-height` / `-sh`: Start height for gradual scaling
- `--start-percent` / `-sp`: Start percent for gradual scaling (default: 100)

### Video & Sequence Options
- `--format` / `-f`: Output image format (png, jpg, bmp, tiff) - default: input format for images, png for video frames
- `--start`: Start time in seconds for video trimming
- `--end`: End time in seconds for video trimming
- `--duration`: Duration in seconds for image sequence generation or video trimming
- `--fps`: Frame rate for image-to-sequence conversion (default: 25)

### Validation Rules
- Cannot specify both width/height and percent - choose one scaling method
- Cannot specify both start-width/height and start-percent - choose one scaling method
- Cannot specify both `--end` and `--duration` for video trimming
- Single image with gradual scaling requires `--duration` to determine frame count
- Batch mode does not support duration, start, end, or gradual scaling parameters

## Output Naming Conventions

The `-cas` suffix is applied **once** to output paths, with behavior varying by input type:

| Input Type | Input Example | Default Output | Notes |
|------------|---------------|----------------|-------|
| Single Image | `/path/to/image.png` | `/path/to/image-cas.png` | Suffix added to filename |
| Single Image (no gradual scaling) | `/path/to/image.png` | `/path/to/image-cas.png` | Same dimensions as input (liquid rescaled) |
| Image Sequence | `/path/to/image.jpg --duration 3` | `/path/to/image-cas/frame-0001.png` | Generates multiple frames in folder |
| Image Folder | `/path/to/folder/` | `/path/to/folder-cas/image.png` | Suffix added to folder name only |
| Video File | `/path/to/video.mp4` | `/path/to/video-cas/frame-0001.png` | Suffix added to output folder name |

**Frame Naming:** Frames are named sequentially as `frame-0001.png`, `frame-0002.png`, etc. Default format is PNG for video/sequence frames, can be overridden with `--format`.

**Frame Count Calculation:**
- Image sequence: `duration × fps` (e.g., 3 seconds × 25 fps = 75 frames)
- Video (full): All frames from video
- Video (trimmed): `(end - start) × video_fps` or `duration × video_fps`

## Architecture

### Project Structure

The application uses a clean, layered architecture with dependency injection for maintainability and testability:

```plaintext
cascaler/
├── Program.cs                          # Entry point with DI setup (~160 lines)
├── Models/
│   ├── ProcessingOptions.cs           # CLI argument encapsulation + ProcessingMode enum
│   ├── ProcessingResult.cs            # Processing outcome data
│   └── VideoFrame.cs                  # Video frame metadata
├── Services/
│   ├── Interfaces/
│   │   ├── IImageProcessingService.cs
│   │   ├── IVideoProcessingService.cs
│   │   ├── IMediaProcessor.cs
│   │   ├── IProgressTracker.cs
│   │   └── IDimensionInterpolator.cs
│   ├── ImageProcessingService.cs      # ImageMagick operations
│   ├── VideoProcessingService.cs      # FFMediaToolkit integration & trimming
│   ├── MediaProcessor.cs              # Batch processing orchestration
│   ├── ProgressTracker.cs             # Centralized ETA calculation
│   └── DimensionInterpolator.cs       # Gradual scaling dimension calculation
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

2. **Models**: Data transfer objects and enums
   - `ProcessingMode`: Enum defining processing context (SingleImage, ImageBatch, Video)
   - `ProcessingOptions`: Encapsulates all CLI parameters (width, height, percent, etc.) and processing mode
   - `ProcessingResult`: Processing outcome with success status and error messages
   - `VideoFrame`: Video frame data with RGB24 pixel data and stride information

3. **Services**: Core business logic with interfaces for testability
   - `ImageProcessingService`: Load, process (liquid rescale), and save images with optional format override
   - `VideoProcessingService`: Extract frames from videos, convert to MagickImage, handle video trimming
   - `MediaProcessor`: Orchestrates parallel batch processing using `Channel<T>`, handles image-to-sequence and gradual scaling
   - `ProgressTracker`: Consolidated progress tracking and ETA calculation
   - `DimensionInterpolator`: Calculates interpolated dimensions for gradual scaling across frames

4. **Infrastructure**: Configuration and utilities
   - `Constants`: Centralized magic numbers, file extensions, defaults
   - `ProcessingConfiguration`: Runtime configuration (thread counts, timeouts)
   - `FFmpegConfiguration`: Automatic FFmpeg library detection

5. **Handlers**: Request handling
   - `CommandHandler`: Validates CLI arguments, detects processing mode, determines output paths, collects input files, orchestrates processing

6. **Utilities**: Helper classes
   - `SharedCounter`: Thread-safe counter for concurrent progress tracking

### Processing Pipeline

**Mode Detection (CommandHandler):**

1. Analyze input path to determine processing mode:
   - Single image file → `ProcessingMode.SingleImage`
   - Single video file → `ProcessingMode.Video`
   - Directory → `ProcessingMode.ImageBatch`
2. Set appropriate default output path:
   - SingleImage: Same directory, filename with `-cas` suffix (e.g., `image-cas.png`)
   - ImageBatch: New folder with `-cas` suffix (e.g., `folder-cas/`)
   - Video: New folder with `-cas` suffix (e.g., `video-cas/`)

**Image Processing:**

1. Load image with ImageMagick
2. Check for image-to-sequence mode (single image + duration specified)
3. If image-to-sequence:
   - Calculate total frames: `duration × fps`
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
6. For each frame (processed in parallel, max 8 threads):
   - Calculate interpolated dimensions if gradual scaling enabled
   - Apply liquid rescale to calculated dimensions
   - If gradual scaling: scale back to original dimensions using regular resize
   - Save frame with format override (default: PNG)
7. Progress bar updates in real-time with "Processing frames" message and accurate ETA based on throughput
8. Save frames with simple sequential naming: `frame-0001.png`, `frame-0002.png`, etc.

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
- User can override with `--format` option for all processing modes
- Format applied during save operation, with extension automatically updated

## Development Notes

### Feature Status

**Video Processing** - Fully functional using FFMediaToolkit:
- **Frame Extraction**: Uses FFMediaToolkit's `MediaFile` and `VideoStream` APIs to extract frames
- **Video Trimming**: Supports start/end time or duration-based trimming
- **Pixel Format**: Configured for RGB24 output for compatibility with ImageMagick
- **Buffer Handling**: Implements stride-aware padding removal for preallocated buffers
- **ImageMagick Import**: Uses `ReadPixels` with `PixelReadSettings` for reliable raw pixel data import
- **Progress Tracking**: Real-time progress bar with dynamic ETA calculation based on frame processing throughput
- **Parallel Processing**: Processes up to 8 frames concurrently with proper progress tracking for each frame

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

## Supported Formats

**Images:** `.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.tif`, `.webp`, `.ico`

**Videos:** `.mp4`, `.avi`, `.mov`, `.mkv`, `.webm`, `.wmv`, `.flv`, `.m4v`

**Output Formats (via --format):** `png`, `jpg`, `bmp`, `tiff`
