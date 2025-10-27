# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**cascaler** is a high-performance batch liquid rescaling tool for images and videos using content-aware seam carving. Built with .NET 10.0, it processes media files in parallel using ImageMagick for liquid rescaling and FFmpeg.AutoGen for video/audio processing.

**✅ MIGRATION STATUS:** Successfully migrated from FFMediaToolkit to FFmpeg.AutoGen 7.1.1 for direct audio filtering (vibrato/tremolo). Compiles with 0 errors/warnings. **Fully functional** - video output with correct frame rate, audio sync, proper AAC-LC encoding, and working vibrato/tremolo effects.

## Quick Reference

```bash
# Build and run
dotnet build
dotnet run -- [options] <input>

# Common examples
dotnet run -- input.jpg -p 75                           # Single image at 75%
dotnet run -- input.mp4 -o output.mp4 -p 75 --vibrato  # Video with audio effects
dotnet run -- input.jpg --duration 3 -sp 100 -p 50     # Image sequence with gradual scaling
```

## Command-Line Options

**Scaling:** `-w/--width`, `-h/--height`, `-p/--percent` (default: 50), `-d/--deltaX` (0-1, default: 1.0), `-r/--rigidity` (default: 1.0), `-t/--threads` (default: 16), `-o/--output`, `--no-progress`

**Gradual Scaling:** `-sw/--start-width`, `-sh/--start-height`, `-sp/--start-percent` (default: 100)

**Video/Sequence:** `-f/--format` (png/jpg/bmp/tiff), `--start`, `--end`, `--duration`, `--fps` (default: 25), `--vibrato`

**Validation Rules:**

- Choose one: width/height OR percent
- Choose one: start-width/height OR start-percent
- Choose one: `--end` OR `--duration`
- Image + gradual scaling requires `--duration`
- Batch mode: no duration/start/end/gradual scaling
- Video output (.mp4/.mkv): video files or image sequences only

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
    "DefaultFps": 25
  },
  "VideoEncoding": {
    "DefaultCRF": 23,            // 0-51, lower = better
    "DefaultPreset": "medium",
    "DefaultPixelFormat": "yuv420p"
  },
  "Output": {
    "Suffix": "-cas",
    "ProgressCharacter": "─"
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
│   └── CommandHandler.cs        # CLI orchestration
└── Utilities/
    ├── SharedCounter.cs
    └── FrameOrderingBuffer.cs   # Frame order during parallel processing
```

### Key Processing Flows

**Mode Detection (CommandHandler):**

1. Single image file → `ProcessingMode.SingleImage`
2. Single video file → `ProcessingMode.Video`
3. Directory → `ProcessingMode.ImageBatch`
4. Validate video output requirements (.mp4/.mkv only for video/sequences)

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

**Windows:** Place DLLs in `bin\Debug\net9.0\runtimes\win-x64\native\` or set `FFMPEG_PATH`

**Recommended:** [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds/releases)

### Technical Details

**Output Paths:** `-cas` suffix applied once. SingleImage: full file path; ImageBatch/Video: folder with sequential frames.

**Video Frame Buffers:** VideoDecoder → PixelFormatConverter (sws_scale) → RGB24 extraction (no padding) → VideoFrame with stride info. Proper cleanup with av_frame_free.

**ImageMagick Import:** Uses `ReadPixels` with `PixelReadSettings` (RGB24, `StorageType.Char`)

**Gradual Scaling:** Linear interpolation: `dimension[i] = start + (target - start) × (i / (total_frames - 1))`. Scale-back feature: liquid rescale to interpolated dimensions → regular resize to original (ensures uniform frame sizes).

**Audio Frame Splitting:** Source frames split to 1024 samples for AAC. Timestamps recalculated for chronological ordering. Prevents "nb_samples > frame_size" errors.

**Frame Ordering:** `FrameOrderingBuffer` accepts frames in any order, releases sequentially to encoder/disk.

## FFmpeg.AutoGen Migration

**Why:** FFMediaToolkit lacks audio filtering (avfilter API) needed for `--vibrato`. FFmpeg.AutoGen provides direct access to native APIs.

**New FFmpeg Subsystem (Services/FFmpeg/):**

- VideoDecoder, AudioDecoder, AudioFilter (NEW), VideoEncoder, AudioEncoder, MediaMuxer, PixelFormatConverter
- All unsafe pointers (AVFrame*, AVPacket*, AVCodecContext*) with proper memory management
- VideoCompilationService rewritten: 958 lines → ~370 lines

**Benefits:** Audio filtering, reduced complexity, direct API control, no wrapper overhead

**Status:** ✅ Compiles (0 errors/warnings), ✅ Fully tested and functional

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

## Testing Checklist

**Tested (Working):**

- ✅ Frame extraction from video (RGB24 via VideoDecoder)
- ✅ Video trimming with --start/--duration
- ✅ Frame output mode (PNG export)
- ✅ Video output mode (MP4 with H.264)
- ✅ Audio extraction and sync
- ✅ Audio trimming alignment
- ✅ Audio encoding (AAC-LC profile at correct sample rate)
- ✅ Audio frame splitting (1024 samples for AAC)
- ✅ Vibrato/tremolo filter (--vibrato flag)
- ✅ Gradual scaling
- ✅ Video timestamp/PTS handling (correct playback speed)
- ✅ Parallel processing (8 threads)
- ✅ Frame ordering (FrameOrderingBuffer)

**Not Yet Tested:**

- ⏸️ MKV output
- ⏸️ Image sequence to video
- ⏸️ Different pixel formats/codecs
- ⏸️ Large files (>1GB)
- ⏸️ Different source sample rates

**Known Risks:** Memory management (leaks, double-free), pixel conversion (sws_scale), container muxing (interleaved packets) with untested formats (MKV, different codecs)

**Debugging:** Check `cascaler config show`, review logs at `~/.config/cascaler/logs/`, test simple inputs first, verify frame counts and audio sync with `ffprobe` or `mediainfo`.

## Supported Formats

**Images:** .jpg, .jpeg, .png, .gif, .bmp, .tiff, .tif, .webp, .ico
**Videos (Input):** .mp4, .avi, .mov, .mkv, .webm, .wmv, .flv, .m4v
**Videos (Output):** .mp4, .mkv
**Frame Formats (--format):** png, jpg, bmp, tiff
**Audio Codecs:** AAC, MP3, AC3, EAC3, MP2 (MP4); all codecs (MKV)
