# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**cascaler** is a high-performance batch liquid rescaling tool for images and videos using content-aware seam carving. Built with .NET 10.0, it processes media files in parallel using ImageMagick for liquid rescaling and FFmpeg.AutoGen for video/audio processing.

**✅ MIGRATION COMPLETE - VALIDATION TESTING IN PROGRESS:** Successfully migrated from FFMediaToolkit to FFmpeg.AutoGen 7.1.1 and completed post-migration cleanup. Compiles with 0 errors/warnings. Core functionality implemented - video output with correct frame rate, audio sync, proper AAC-LC encoding, and working vibrato/tremolo effects. Codebase cleaned of all dead code, deprecated methods, and migration artifacts (~220+ lines removed). **Awaiting full end-to-end testing of all commands and parameters.**

## Quick Reference

```bash
# Build and run
dotnet build
dotnet run -- [options] <input>

# Common examples
dotnet run -- input.jpg -p 75                            # Single image at 75%
dotnet run -- input.mp4 -o output.mp4 -p 75 --vibrato   # Video with audio effects
dotnet run -- input.jpg --duration 3 -sp 100 -p 50      # Image sequence with gradual scaling
dotnet run -- images/ -o output.mp4 -sp 75 -p 25        # Directory to video with gradual scaling
dotnet run -- images/ -sp 75 -p 25                       # Batch images with gradual scaling
dotnet run -- images/ --scale-back -o output.mp4        # Apply effects, scale back to 100%
```

## Command-Line Options

**Scaling:** `-w/--width`, `-h/--height`, `-p/--percent` (default: 50), `-d/--deltaX` (0-1, default: 1.0), `-r/--rigidity` (default: 1.0), `-t/--threads` (default: 16), `-o/--output`, `--no-progress`, `--scale-back`

**Gradual Scaling:** `-sw/--start-width`, `-sh/--start-height`, `-sp/--start-percent` (default: same as `-p`)

**Video/Sequence:** `-f/--format` (png/jpg/bmp/tiff), `--start`, `--end`, `--duration`, `--fps` (default: 25), `--vibrato`

**Scale-Back Feature:** `--scale-back` scales processed frames back to original 100% dimensions (ignoring start/end percent values). Useful for applying liquid rescaling effects while maintaining original dimensions.

**Validation Rules:**

- Choose one: width/height OR percent
- Choose one: start-width/height OR start-percent
- Choose one: `--end` OR `--duration`
- Single image + gradual scaling requires `--duration`
- Batch mode: no duration/start/end parameters
- Video output (.mp4/.mkv): video files, image sequences, or directory-to-video only

## Configuration

**Locations:**

- Unix/macOS: `~/.config/cascaler/appsettings.json`
- Windows: `%APPDATA%\cascaler\appsettings.json`

**Commands:**

```bash
cascaler config show                             # Show effective config
cascaler config path                             # Show config file path
cascaler config init [--detect-ffmpeg]          # Create user config
cascaler config export <file> [--detect-ffmpeg] # Export config
```

**Priority:** Embedded defaults → User config → CLI arguments

**Key Sections:**

```json
{
  "FFmpeg": {
    "LibraryPath": "",           // Empty = auto-detect
    "EnableAutoDetection": true  // Fallback if path invalid
  },
  "Processing": {
    "MaxImageThreads": 16,
    "MaxVideoThreads": 8,
    "ProcessingTimeoutSeconds": 30,
    "DefaultScalePercent": 50,
    "DefaultFps": 25,
    "DefaultVideoFrameFormat": "png",
    "DefaultImageOutputFormat": "",  // Empty = preserve input format
    "DefaultDeltaX": 1.0,        // Seam curvature (0-1)
    "DefaultRigidity": 1.0,      // Seam bias (0-10)
    "DefaultScaleBack": false,   // Scale back to 100%
    "DefaultVibrato": false      // Audio effects
  },
  "VideoEncoding": {
    "DefaultCRF": 23,            // 0-51, lower = better
    "DefaultPreset": "medium",
    "DefaultPixelFormat": "yuv420p",
    "DefaultCodec": "libx264"
  },
  "Output": {
    "Suffix": "-cas",
    "ProgressCharacter": "─",
    "ShowEstimatedDuration": true
  }
}
```

## Logging

**Dual-output strategy** integrated with ShellProgressBar:

- **Console:** Information+ (clean messages, warnings/errors prefixed)
- **Files:** `~/.config/cascaler/logs/cascaler-YYYYMMDD.log` (Debug+, 7-day retention)

**Progress-bar integration:** Logs route through `progressBar.WriteLine()` when active. Use `--no-progress` to disable progress bar.

**Custom components:** `ProgressBarContext`, `ProgressBarAwareConsoleLogger/Provider`, `FileLoggerProvider`

## Architecture

### Project Structure

```plaintext
cascaler/
├── Program.cs                   # Entry point, DI, configuration
├── Models/                      # ProcessingOptions, ProcessingResult, VideoFrame
├── Services/
│   ├── FFmpeg/                  # Native FFmpeg.AutoGen implementations
│   │   ├── VideoDecoder.cs      # avformat/avcodec frame extraction
│   │   ├── AudioDecoder.cs      # Float planar audio extraction
│   │   ├── AudioFilter.cs       # Vibrato/tremolo via avfilter
│   │   ├── VideoEncoder.cs      # H.264/H.265 encoding
│   │   ├── AudioEncoder.cs      # AAC encoding (1024 samples)
│   │   ├── MediaMuxer.cs        # MP4/MKV container muxing
│   │   └── PixelFormatConverter.cs # RGB24 ↔ YUV420P via sws_scale
│   ├── ImageProcessingService.cs
│   ├── VideoProcessingService.cs
│   ├── VideoCompilationService.cs # Unified video+audio encoding
│   ├── MediaProcessor.cs        # Batch processing orchestration
│   ├── ProgressTracker.cs       # ETA calculation
│   └── DimensionInterpolator.cs # Gradual scaling calculations
├── Infrastructure/
│   ├── Constants.cs
│   ├── ConfigurationHelper.cs   # Multi-source config builder
│   ├── FFmpegConfiguration.cs   # FFmpeg path detection (cached)
│   ├── ProgressBarContext.cs    # Active progress bar tracking
│   ├── ProgressBarAwareConsoleLogger.cs
│   ├── FileLoggerProvider.cs
│   └── Options/                 # FFmpegOptions, ProcessingSettings, etc.
├── Handlers/
│   ├── CommandHandler.cs        # CLI orchestration
│   └── ConfigCommandHandler.cs  # Config management (show, init, export, path)
└── Utilities/
    ├── SharedCounter.cs
    └── FrameOrderingBuffer.cs   # Frame order during parallel processing
```

### Key Processing Flows

**Mode Detection (CommandHandler):**

1. Single image file → `ProcessingMode.SingleImage`
2. Single video file → `ProcessingMode.Video`
3. Directory + video output (.mp4/.mkv) → `ProcessingMode.SingleImage` (directory-to-video)
4. Directory + no video output → `ProcessingMode.ImageBatch`
5. Validate video output requirements (.mp4/.mkv only for video/sequences/directory-to-video)

**Image Processing:**

- Load with ImageMagick → check image-to-sequence mode (duration specified)
- If sequence: calculate frames (`duration × fps`), apply gradual scaling if enabled
- If video output: encode with FFmpeg.AutoGen (H.264, no audio)
- If frame output: save as image files (sequential naming)
- Single image: apply liquid rescale, save to output path

**Video Processing:**

1. Initialize FFmpeg, calculate frame range (trimming support)
2. Extract frames as RGB24 via VideoDecoder (avformat/avcodec)
3. Convert to MagickImage using `ReadPixels`
4. **Video output mode:**
   - Extract audio with AudioDecoder
   - Apply vibrato/tremolo filter if `--vibrato` (AudioFilter)
   - Split audio to 1024-sample AAC frames
   - Parallel frame processing (max 8 threads) → FrameOrderingBuffer
   - Encode video (VideoEncoder) and audio (AudioEncoder) → MediaMuxer
   - Single-pass synchronized muxing
5. **Frame output mode:** Save processed frames as images (default: PNG)

### Concurrency

- `Channel<T>` work queue + `SemaphoreSlim` concurrency control
- Default: 16 threads (images), 8 threads (video frames)
- Real-time progress tracking with ETA (updates after 3+ items)
- `FrameOrderingBuffer` maintains temporal order during parallel processing

### Dependencies

- **ImageMagick.NET**: Liquid rescaling
- **FFmpeg.AutoGen 7.1.1**: P/Invoke bindings to FFmpeg
- **FFmpeg 7.x libraries**: libavcodec, libavformat, libavutil, libswscale, libswresample, libavfilter
- **System.CommandLine**: CLI parsing
- **ShellProgressBar**: Progress visualization
- **Microsoft.Extensions.{DependencyInjection,Logging}**: DI and logging

### FFmpeg Requirements

**Search order:** `FFMPEG_PATH` env var → common paths (macOS: `/opt/homebrew/opt/ffmpeg@7/lib`, etc.; Windows: `C:\Program Files\ffmpeg\lib`, etc.)

**Windows:** Place DLLs in `bin\Debug\net10.0\runtimes\win-x64\native\` or set `FFMPEG_PATH`

**Recommended:** [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds/releases)

### Technical Details

**Output Paths:** `-cas` suffix applied once. SingleImage: full file path; ImageBatch/Video: folder with sequential frames.

**Video Frame Buffers:** VideoDecoder → PixelFormatConverter (sws_scale) → RGB24 extraction (no padding) → VideoFrame with stride info. Proper cleanup with av_frame_free.

**ImageMagick Import:** Uses `ReadPixels` with `PixelReadSettings` (RGB24, `StorageType.Char`)

**Gradual Scaling:** Linear interpolation: `dimension[i] = start + (target - start) × (i / (total_frames - 1))`.

**Scale-back feature:** Liquid rescale to interpolated dimensions → regular resize to final dimensions (ensures uniform output sizes). Two modes:

- **Default:** Scale back to `max(start, end)` dimensions (maintains largest dimension from gradual scaling)
- **With `--scale-back`:** Scale back to original 100% dimensions (ignores start/end percent values)

Supported for:

- Single image to video/frames (with `--duration`)
- Video to video/frames
- Directory to video
- Directory to images (batch processing)
- Single image processing (with `--scale-back` only)

**Audio Frame Splitting:** Source frames split to 1024 samples for AAC. Timestamps recalculated for chronological ordering. Prevents "nb_samples > frame_size" errors.

**Frame Ordering:** `FrameOrderingBuffer` accepts frames in any order, releases sequentially to encoder/disk.

## FFmpeg.AutoGen Migration

**Why:** FFMediaToolkit lacks audio filtering (avfilter API) needed for `--vibrato`. FFmpeg.AutoGen provides direct access to native APIs.

**New FFmpeg Subsystem (Services/FFmpeg/):**

- VideoDecoder, AudioDecoder, AudioFilter (NEW), VideoEncoder, AudioEncoder, MediaMuxer, PixelFormatConverter
- All unsafe pointers (AVFrame*, AVPacket*, AVCodecContext*) with proper memory management
- VideoCompilationService rewritten: 958 lines → ~370 lines

**Benefits:** Audio filtering, reduced complexity, direct API control, no wrapper overhead

**Status:** ✅ Compiles (0 errors/warnings), ✅ Core features tested and working, ✅ Post-migration cleanup completed, ⏸️ Full validation testing pending

**Issues Fixed During Testing:**

1. **VideoEncoder flush handling** - Fixed NullReferenceException when passing null MagickImage during encoder flush. Added null check and proper handling for receiving remaining packets.

2. **Audio timestamp calculation** - AudioDecoder timestamps were all 0 because `swr_convert_frame` doesn't preserve PTS. Fixed by reading timestamp from original frame before conversion.

3. **Video timestamp rescaling** - Video was playing at 12,800 FPS instead of 50 FPS due to incorrect timestamp rescaling in MediaMuxer. Fixed by:
   - Adding TimeBase properties to VideoEncoder and AudioEncoder
   - Adding SetVideoEncoderTimeBase/SetAudioEncoderTimeBase methods to MediaMuxer
   - Properly rescaling packet timestamps from encoder time_base to stream time_base

4. **Audio filter format mismatch** - AudioFilter was outputting packed format (FLT) instead of planar format (FLTP), causing NULL pointer for channel 1. Fixed by:
   - Adding explicit format constraint to filter sink using `av_opt_set_bin`
   - Forcing output to AV_SAMPLE_FMT_FLTP (float planar)
   - Proper channel layout configuration with layout description string

5. **AAC encoder metadata** - Container showed "ER Parametric" profile at 7,350 Hz instead of "AAC LC" at 48,000 Hz. Fixed by:
   - Adding `SetAudioEncoderParameters()` method to MediaMuxer
   - Using `avcodec_parameters_from_context()` to copy encoder's codec parameters (profile, extradata, sample rate) to output stream
   - Calling this after encoder initialization but before writing header

6. **Gradual scaling dimension mismatch** - Video encoder was initialized with original dimensions but frames were scaled to target dimensions without scale-back. Fixed by:
   - Changed encoder dimensions to use `Math.Max(startWidth, endWidth)` instead of always using original
   - Only apply scale-back when start != end dimensions (true gradual scaling)
   - Applied same logic to all processing modes: video-to-video, image-to-video, directory-to-video, and batch images

7. **Gradual scaling for batch images** - Batch image processing blocked gradual scaling entirely. Fixed by:
   - Removed validation restriction preventing gradual scaling in batch mode
   - Added file index tracking through processing pipeline
   - Calculate interpolated dimensions per image: `dimension[i] = start + (end - start) × (i / (total - 1))`
   - Apply scale-back to max(start, end) for uniform output dimensions

8. **Scale-back parameter implementation** - Added `--scale-back` parameter to scale processed frames back to original 100% dimensions:
   - Added `ScaleBack` property to ProcessingOptions model
   - Added CLI option with configuration default support
   - Updated all 6 video/image processing methods to handle scale-back flag
   - Works with and without gradual scaling enabled
   - Extended configuration system with new defaults: DefaultScaleBack, DefaultVibrato, DefaultDeltaX, DefaultRigidity, DefaultImageOutputFormat

## Post-Migration Cleanup

**Completed:** All dead code, deprecated methods, and migration artifacts removed from the codebase.

**Dead Code Removed:**

1. **IVideoCompilationService.cs** - Removed 4 unused method signatures:
   - `ExtractAudioFromVideoAsync()` - No longer needed with unified encoding
   - `StartStreamingEncoderAsync()` - Replaced by `StartStreamingEncoderWithAudioAsync()`
   - `MergeVideoWithAudioAsync()` - No longer needed with unified encoding
   - `DetermineOutputContainerFromVideoAsync()` - Never called, hardcoded to return ".mp4"

2. **VideoCompilationService.cs** - Removed 4 stub method implementations throwing `NotImplementedException` (~40 lines)

3. **Infrastructure/CleanConsoleFormatter.cs** - Deleted entire file (~100 lines)
   - Never registered in DI, replaced by `ProgressBarAwareConsoleLogger`

4. **DimensionInterpolator.cs** - Removed `CalculateFrameDimensions()` method from interface and implementation
   - Never called, superseded by `GetStartDimensions()`/`GetEndDimensions()`

5. **MediaProcessor.cs** - Updated to use new `StartStreamingEncoderWithAudioAsync()` method
   - Fixed breaking change from removed `StartStreamingEncoderAsync()`

**Code Refactoring:**

1. **ConfigCommandHandler.cs** - Extracted duplicate config serialization logic
   - Created `BuildConfigurationObject()` helper method
   - Eliminated ~60 lines of duplication between `InitConfig()` and `ExportConfig()`

**Documentation Cleanup:**

1. Removed all migration-related comments referencing "FFMediaToolkit" from code files:
   - AudioEncoder.cs, AudioDecoder.cs, VideoEncoder.cs, VideoDecoder.cs
   - MediaMuxer.cs, VideoCompilationService.cs, VideoProcessingService.cs

**Total Impact:** ~220+ lines of dead/duplicate code removed, 0 breaking changes (all functionality preserved)

**Build Status:** ✅ 0 Errors, 0 Warnings

## Testing Status

**Development Testing (Completed):**

These features were tested during migration and development, confirmed working:

- ✅ Frame extraction from video (RGB24 via VideoDecoder)
- ✅ Video trimming with --start/--duration
- ✅ Frame output mode (PNG export)
- ✅ Video output mode (MP4 with H.264)
- ✅ Audio extraction and sync
- ✅ Audio trimming alignment
- ✅ Audio encoding (AAC-LC profile at correct sample rate)
- ✅ Audio frame splitting (1024 samples for AAC)
- ✅ Vibrato/tremolo filter (--vibrato flag)
- ✅ Gradual scaling (video-to-video, image-to-video, directory-to-video, batch images)
- ✅ Scale-back to max(start, end) for uniform output dimensions
- ✅ Scale-back to 100% with --scale-back parameter (images and video)
- ✅ Video timestamp/PTS handling (correct playback speed)
- ✅ Parallel processing (8 threads for video, 16 for images)
- ✅ Frame ordering (FrameOrderingBuffer)
- ✅ Directory-to-video conversion
- ✅ Batch image processing with gradual scaling

**Full Validation Testing (Pending):**

The following require end-to-end testing with various parameter combinations:

- ⏸️ All command-line option combinations and validation rules
- ⏸️ Single image processing (all scaling options)
- ⏸️ Batch image processing (directory mode)
- ⏸️ Image-to-video sequence generation
- ⏸️ Video-to-frames extraction (various formats)
- ⏸️ Video-to-video processing with audio preservation
- ⏸️ Gradual scaling across sequences and videos
- ⏸️ Video trimming with --start/--end/--duration combinations
- ⏸️ MKV output format
- ⏸️ Different pixel formats and codecs
- ⏸️ Large files (>1GB)
- ⏸️ Different audio sample rates and codecs
- ⏸️ Configuration file system (init, export, show, path commands)
- ⏸️ Progress bar and logging output
- ⏸️ Error handling and edge cases
- ⏸️ Performance under heavy parallel workloads

**Known Risks:**

- Memory management (leaks, double-free) with untested formats
- Pixel conversion edge cases (sws_scale)
- Container muxing (interleaved packets) with untested codecs
- Audio sync with non-standard sample rates

**Debugging Tools:**

- `cascaler config show` - View effective configuration
- `~/.config/cascaler/logs/` - Review detailed logs (7-day retention)
- `ffprobe` / `mediainfo` - Verify output file properties, frame counts, audio sync
- `--no-progress` - Disable progress bar for cleaner log output

## Supported Formats

**Images:** .jpg, .jpeg, .png, .gif, .bmp, .tiff, .tif, .webp, .ico
**Videos (Input):** .mp4, .avi, .mov, .mkv, .webm, .wmv, .flv, .m4v
**Videos (Output):** .mp4, .mkv
**Frame Formats (--format):** png, jpg, bmp, tiff
**Audio Codecs:** AAC, MP3, AC3, EAC3, MP2 (MP4); all codecs (MKV)
