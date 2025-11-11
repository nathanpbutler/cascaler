# Architecture

Technical overview of cascaler's architecture, processing model, and implementation details.

## Technology Stack

Built on .NET 10.0 with modern async/await patterns and dependency injection:

- **ImageMagick.NET** - Content-aware liquid rescaling (seam carving)
- **FFmpeg.AutoGen 7.1.1** - Direct P/Invoke bindings to native FFmpeg libraries
- **System.CommandLine** - Modern CLI argument parsing and validation
- **ShellProgressBar** - Real-time progress visualization with ETA
- **Microsoft.Extensions.{DependencyInjection, Logging}** - DI and structured logging

### FFmpeg Libraries

Direct access to native FFmpeg APIs via FFmpeg.AutoGen:

- **libavcodec**: Video/audio encoding and decoding
- **libavformat**: Container format muxing/demuxing
- **libavutil**: Utility functions, memory management
- **libswscale**: Pixel format conversion and scaling
- **libswresample**: Audio sample format conversion and resampling
- **libavfilter**: Audio/video filtering (vibrato, tremolo)

## Project Structure

```plaintext
cascaler/
├── Program.cs                   # Entry point, DI, configuration
├── Models/                      # Data models
│   ├── ProcessingOptions.cs
│   ├── ProcessingResult.cs
│   └── VideoFrame.cs
├── Services/
│   ├── FFmpeg/                  # Native FFmpeg.AutoGen implementations
│   │   ├── VideoDecoder.cs      # avformat/avcodec frame extraction
│   │   ├── AudioDecoder.cs      # Float planar audio extraction
│   │   ├── AudioFilter.cs       # Vibrato/tremolo via avfilter
│   │   ├── VideoEncoder.cs      # H.264/H.265 encoding
│   │   ├── AudioEncoder.cs      # AAC encoding (1024 samples)
│   │   └── MediaMuxer.cs        # MP4/MKV container muxing
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
│   └── Options/                 # Configuration POCOs
│       ├── FFmpegOptions.cs
│       ├── ProcessingSettings.cs
│       ├── VideoEncodingOptions.cs
│       └── OutputOptions.cs
├── Handlers/
│   ├── CommandHandler.cs        # CLI orchestration
│   └── ConfigCommandHandler.cs  # Config management
└── Utilities/
    ├── SharedCounter.cs
    ├── FrameOrderingBuffer.cs   # Frame order during parallel processing
    └── PixelFormatConverter.cs  # RGB24 ↔ YUV420P via sws_scale
```

## Processing Model

### Mode Detection

cascaler automatically detects the processing mode based on the input type and options:

**Mode Detection Flow (CommandHandler):**

1. **Single image file** → `ProcessingMode.SingleImage`
2. **Single video file** → `ProcessingMode.Video`
3. **Directory + video output** (`.mp4`/`.mkv`) → `ProcessingMode.SingleImage` (directory-to-video)
4. **Directory + no video output** → `ProcessingMode.ImageBatch`

**Validation:**

- Video output requires `.mp4` or `.mkv` extension
- Video output is only allowed for: video files, image sequences, or directory-to-video

### Concurrency

Producer-consumer pattern using `Channel<T>` with configurable concurrency:

- **Default:** 16 threads for images, 8 for video frames
- **Thread-safe frame ordering** via `FrameOrderingBuffer` maintains temporal sequence
- **Real-time progress tracking** with ETA (updates after 3+ items are processed)
- **Memory-efficient** streaming processing (frames processed and released immediately)

**Concurrency Control:**

```csharp
SemaphoreSlim(maxDegreeOfParallelism)
Channel<T>.CreateBounded(capacity)
```

### Configuration System

Multi-source configuration with priority:

1. **Embedded defaults** (built into executable)
2. **User configuration file** (`~/.config/cascaler/appsettings.json`)
3. **Command-line arguments** (highest priority)

**Configuration Builder:**

```csharp
ConfigurationHelper.BuildConfiguration()
  → AddJsonFile("appsettings.json", embedded: true)
  → AddJsonFile(userConfigPath, optional: true)
  → Build()
```

**FFmpeg Path Detection:**

- Cached in `FFmpegConfiguration` (singleton)
- Searches common paths by platform (macOS, Linux, Windows)
- Respects `FFMPEG_PATH` environment variable
- Configurable via `FFmpeg.LibraryPath` setting

### Logging System

Dual-output strategy integrated with ShellProgressBar:

**Console Logger:**

- Information level and above
- Routes through `progressBar.WriteLine()` when active
- Clean messages (warnings/errors prefixed)
- Disabled with `--no-progress`

**File Logger:**

- Debug level and above
- Location: `~/.config/cascaler/logs/cascaler-YYYYMMDD.log`
- 7-day retention with automatic cleanup
- Always enabled (regardless of progress bar)

**Custom Components:**

- `ProgressBarContext`: Tracks active progress bar instance
- `ProgressBarAwareConsoleLogger/Provider`: Progress-bar-aware console output
- `FileLoggerProvider`: File-based logging with retention policy

## Processing Flows

### 1. Single Image Processing

```plaintext
Input: single.jpg

CommandHandler
  → MediaProcessor.ProcessImagesAsync()
    → ImageProcessingService.ProcessImageAsync()
      → Load with ImageMagick
      → Apply liquid rescale
      → Save to output path

Output: single-cas.jpg
```

### 2. Image Sequence (Image to Video/Frames)

```plaintext
Input: single.jpg --duration 3 -sp 100 -p 50

CommandHandler
  → Check duration specified → ProcessingMode.SingleImage (sequence mode)
    → MediaProcessor.ProcessImagesAsync()
      → Calculate total frames: duration × fps
      → DimensionInterpolator.GetInterpolatedDimensions()
        → Linear interpolation: start → end over frames
      → For each frame:
        → ImageProcessingService.ProcessImageAsync()
        → Apply liquid rescale to interpolated dimensions
        → If --scale-back: resize to original dimensions
      → If video output (.mp4):
        → VideoCompilationService.StartStreamingEncoderWithAudioAsync()
        → Encode frames to H.264 (no audio)
      → If frame output:
        → Save as sequential images

Output: single-cas-0001.png, 0002.png, ... or single-cas.mp4
```

### 3. Video Processing

```plaintext
Input: video.mp4 -o output.mp4 -sp 100 -p 50 --vibrato

CommandHandler
  → ProcessingMode.Video
    → MediaProcessor.ProcessVideosAsync()
      → VideoProcessingService.ProcessVideoAsync()
        → VideoDecoder.Initialize()
          → Extract frame count, FPS, dimensions
          → Calculate trimming range (--start/--end/--duration)
        → AudioDecoder.Initialize()
          → Extract audio stream (float planar)
          → Align with video trimming
        → If --vibrato:
          → AudioFilter.Initialize()
          → Apply vibrato/tremolo effects
        → For each video frame (parallel, max 8 threads):
          → VideoDecoder.DecodeNextFrame()
            → avformat_read_frame()
            → avcodec_send_packet() / avcodec_receive_frame()
            → PixelFormatConverter.ConvertToRgb24()
              → sws_scale(YUV420P → RGB24)
          → Convert to MagickImage (ReadPixels)
          → DimensionInterpolator.GetInterpolatedDimensions()
          → ImageProcessingService.ProcessImageAsync()
            → Apply liquid rescale
          → If --scale-back: resize to max(start, end) dimensions
          → FrameOrderingBuffer.AddFrame()
        → VideoCompilationService.StartStreamingEncoderWithAudioAsync()
          → VideoEncoder.Initialize()
            → H.264 encoding (configurable CRF/preset)
          → AudioEncoder.Initialize()
            → AAC encoding (1024 samples per frame)
          → MediaMuxer.Initialize()
            → MP4/MKV container
          → For each ordered frame:
            → PixelFormatConverter.ConvertFromRgb24()
              → sws_scale(RGB24 → YUV420P)
            → VideoEncoder.EncodeFrame()
              → avcodec_send_frame() / avcodec_receive_packet()
            → MediaMuxer.WriteVideoPacket()
              → av_interleaved_write_frame()
          → For each audio frame:
            → Split to 1024 samples
            → AudioEncoder.EncodeAudioFrame()
            → MediaMuxer.WriteAudioPacket()
          → Flush encoders
          → MediaMuxer.Finalize()
            → av_write_trailer()

Output: output.mp4
```

### 4. Batch Processing

```plaintext
Input: /images -sp 75 -p 25

CommandHandler
  → ProcessingMode.ImageBatch
    → MediaProcessor.ProcessImagesAsync()
      → Enumerate image files
      → For each file index i (parallel, max 16 threads):
        → DimensionInterpolator.GetInterpolatedDimensions()
          → dimension[i] = start + (end - start) × (i / (total - 1))
        → ImageProcessingService.ProcessImageAsync()
          → Apply liquid rescale to interpolated dimensions
          → If --scale-back: resize to max(start, end) dimensions
        → Save to output directory

Output: /images-cas/image1.jpg, image2.jpg, ...
```

### 5. Directory to Video

```plaintext
Input: /images -o output.mp4 -sp 75 -p 25

CommandHandler
  → Directory + video output → ProcessingMode.SingleImage (directory-to-video)
    → MediaProcessor.ProcessImagesAsync()
      → Enumerate image files (sorted)
      → For each file index i:
        → DimensionInterpolator.GetInterpolatedDimensions()
        → ImageProcessingService.ProcessImageAsync()
        → Apply liquid rescale
        → If --scale-back: resize to max(start, end) dimensions
      → VideoCompilationService.StartStreamingEncoderWithAudioAsync()
        → Encode frames to H.264 (no audio)

Output: output.mp4
```

## Video Processing Pipeline

### VideoDecoder (Frame Extraction)

Uses FFmpeg.AutoGen for native API access:

```csharp
avformat_open_input()      // Open video file
avformat_find_stream_info() // Read stream info
avcodec_find_decoder()      // Find video decoder
avcodec_open2()             // Initialize decoder
avformat_read_frame()       // Read packet
avcodec_send_packet()       // Send to decoder
avcodec_receive_frame()     // Receive decoded frame
```

**Output:** RGB24 frames via `PixelFormatConverter.ConvertToRgb24()`

### AudioDecoder (Audio Extraction)

Extracts float planar audio with timestamp preservation:

```csharp
avcodec_find_decoder(CODEC_ID_AAC) // Find audio decoder
avcodec_open2()                     // Initialize decoder
swr_alloc_set_opts2()               // Setup resampler
swr_init()                          // Initialize resampler
swr_convert_frame()                 // Convert to float planar
```

**Output:** `AudioFrame` with float[] data, timestamp, and sample rate

**Trimming:** Audio frames aligned with video segment (frame-accurate)

### AudioFilter (Effects)

Vibrato and tremolo effects via libavfilter:

```csharp
avfilter_graph_alloc()                // Create filter graph
avfilter_graph_create_filter()        // Create source/sink buffers
avfilter_link()                       // Link filters
avfilter_graph_parse_ptr("vibrato=f=5:d=0.5,tremolo=f=5:d=0.5")
avfilter_graph_config()               // Initialize graph
av_buffersrc_add_frame_flags()        // Push frame
av_buffersink_get_frame()             // Pull filtered frame
```

**Output:** Filtered float planar audio

### VideoEncoder (H.264/H.265 Encoding)

Configurable quality and preset:

```csharp
avcodec_find_encoder_by_name("libx264") // Find encoder
av_opt_set(ctx->priv_data, "crf", "23") // Set quality
av_opt_set(ctx->priv_data, "preset", "medium")
avcodec_open2()                         // Initialize encoder
avcodec_send_frame()                    // Send frame
avcodec_receive_packet()                // Receive encoded packet
```

**Input:** YUV420P frames via `PixelFormatConverter.ConvertFromRgb24()`

**Settings:** CRF (0-51), preset (ultrafast → veryslow), pixel format

### AudioEncoder (AAC Encoding)

AAC-LC profile with proper frame splitting:

```csharp
avcodec_find_encoder(AV_CODEC_ID_AAC)  // Find AAC encoder
ctx->profile = FF_PROFILE_AAC_LOW       // AAC-LC profile
ctx->sample_fmt = AV_SAMPLE_FMT_FLTP    // Float planar
ctx->frame_size = 1024                  // AAC frame size
avcodec_open2()                         // Initialize encoder
```

**Frame Splitting:** Source audio split to 1024-sample frames with recalculated timestamps

**Why:** Prevents "nb_samples > frame_size" errors, maintains chronological order

### MediaMuxer (Container Muxing)

Single-pass synchronized video+audio muxing:

```csharp
avformat_alloc_output_context2()    // Create output context
avio_open()                          // Open output file
avformat_new_stream()                // Add video stream
avformat_new_stream()                // Add audio stream
avcodec_parameters_from_context()   // Copy encoder parameters
avformat_write_header()              // Write container header
av_interleaved_write_frame()         // Write A/V packets (interleaved)
av_write_trailer()                   // Finalize container
```

**Timestamp Rescaling:**

```csharp
packet.pts = av_rescale_q(packet.pts, encoderTimeBase, streamTimeBase)
packet.dts = av_rescale_q(packet.dts, encoderTimeBase, streamTimeBase)
packet.duration = av_rescale_q(packet.duration, encoderTimeBase, streamTimeBase)
```

**Why:** Ensures correct playback speed and A/V sync

### PixelFormatConverter (Color Conversion)

RGB24 ↔ YUV420P conversion via libswscale:

```csharp
sws_getContext(
  srcW, srcH, AV_PIX_FMT_RGB24,
  dstW, dstH, AV_PIX_FMT_YUV420P,
  SWS_BILINEAR
)
sws_scale(context, srcData, srcStride, 0, srcH, dstData, dstStride)
```

**RGB24 → ImageMagick:**

```csharp
ReadPixels(rgbData, new PixelReadSettings(width, height, StorageType.Char, "RGB"))
```

**ImageMagick → RGB24:**

```csharp
WritePixels(0, 0, width, height, "RGB")
```

## Memory Management

### Native Resource Cleanup

All FFmpeg structs are properly cleaned up:

```csharp
av_frame_free(&frame)
av_packet_free(&packet)
avcodec_free_context(&codecContext)
avformat_close_input(&formatContext)
sws_freeContext(swsContext)
swr_free(&swrContext)
avfilter_graph_free(&filterGraph)
```

**Pattern:** All native resources wrapped in `IDisposable` implementations

### Frame Ordering Buffer

Maintains temporal order during parallel processing:

```csharp
public void AddFrame(int frameIndex, VideoFrame frame)
{
  _frames[frameIndex] = frame;
  while (_frames.ContainsKey(_nextFrameToRelease))
  {
    yield return _frames[_nextFrameToRelease];
    _frames.Remove(_nextFrameToRelease);
    _nextFrameToRelease++;
  }
}
```

**Benefits:**

- Process frames in parallel (any order)
- Release frames sequentially (correct order)
- Memory-efficient (releases frames as soon as possible)

## Gradual Scaling

### Dimension Interpolation

Linear interpolation over time:

```csharp
public (int width, int height) GetInterpolatedDimensions(int frameIndex, int totalFrames)
{
  if (totalFrames == 1)
    return (_endWidth, _endHeight);

  double progress = (double)frameIndex / (totalFrames - 1);
  int width = (int)(_startWidth + (_endWidth - _startWidth) * progress);
  int height = (int)(_startHeight + (_endHeight - _startHeight) * progress);

  return (width, height);
}
```

**Example:** 100% → 50% over 75 frames

- Frame 0: 100%
- Frame 37: 75%
- Frame 74: 50%

### Scale-Back Feature

Two modes:

**1. Default Scale-Back (gradual scaling with uniform output):**

```csharp
targetDimensions = max(startDimensions, endDimensions)
liquidRescale(frame, interpolatedDimensions)
regularResize(frame, targetDimensions)
```

**2. Scale-Back to 100% (--scale-back flag):**

```csharp
targetDimensions = originalDimensions (100%)
liquidRescale(frame, interpolatedDimensions)
regularResize(frame, targetDimensions)
```

**Why:** Ensures uniform output dimensions while preserving liquid rescaling effect

**Supported Modes:**

- Single image to video/frames (with `--duration`)
- Video to video/frames
- Directory to video
- Directory to images (batch processing)
- Single image processing (with `--scale-back` only)

## Supported Formats

### Input Images

`.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.tif`, `.webp`, `.ico`

### Input Videos

`.mp4`, `.avi`, `.mov`, `.mkv`, `.webm`, `.wmv`, `.flv`, `.m4v`

**Detection:** Based on file extension (case-insensitive)

### Output Videos

`.mp4`, `.mkv`

**Codecs:**

- MP4: H.264, AAC
- MKV: H.264/H.265, AAC/MP3/AC3/etc.

**Why limited:** H.264 + AAC provides the best compatibility

### Frame Formats

`png` (default), `jpg`, `bmp`, `tiff`

**Specified via:** `--format` or `DefaultVideoFrameFormat` config

## FFmpeg Requirements

### Required Libraries

FFmpeg 7.x shared libraries:

- `libavcodec` (v61)
- `libavformat` (v61)
- `libavutil` (v59)
- `libswscale` (v8)
- `libswresample` (v5)
- `libavfilter` (v10)

### Installation Paths

**macOS (Homebrew):**

```bash
brew install ffmpeg@7
# Libraries: /opt/homebrew/opt/ffmpeg@7/lib
```

**Linux (Ubuntu/Debian):**

```bash
sudo apt install ffmpeg
# Libraries: /usr/lib/x86_64-linux-gnu
```

**Windows:**

- Download: [https://www.gyan.dev/ffmpeg/builds](https://www.gyan.dev/ffmpeg/builds)
- Extract DLLs to: `bin\Debug\net10.0\runtimes\win-x64\native\`
- Or set `FFMPEG_PATH` environment variable

### Path Detection

Search order:

1. `FFMPEG_PATH` environment variable
2. `FFmpeg.LibraryPath` configuration setting
3. Common paths by platform (see Configuration.md)

**Caching:** Path detection results cached in `FFmpegConfiguration` singleton

## Performance Characteristics

### Image Processing

- **Bottleneck:** Liquid rescaling (CPU-intensive)
- **Optimization:** Parallel processing (16 threads default)
- **Memory:** ~50-100 MB per thread (depends on image size)
- **Speed:** ~1-5 seconds per image (1920x1080 @ 50%)

### Video Processing

- **Bottleneck:** Liquid rescaling + video encoding
- **Optimization:** Parallel frame processing (8 threads default)
- **Memory:** ~200-500 MB total (frame buffers + encoder)
- **Speed:** ~0.5-2 fps (1080p @ 50%, H.264 medium preset)

### Tuning Parameters

**Faster Processing:**

- Lower `--delta-x` (straighter seams, less accurate)
- Lower `--rigidity` (less bias, faster)
- Increase `--threads` (more CPU cores)
- Use `fast` or `veryfast` encoder preset

**Better Quality:**

- Higher `--delta-x` (more curved seams, better accuracy)
- Higher `--rigidity` (more bias, better seam selection)
- Lower CRF (better video quality, larger files)
- Use `slow` or `veryslow` encoder preset

## Error Handling

### FFmpeg Errors

Native FFmpeg errors logged with error codes:

```csharp
int ret = avformat_open_input();
if (ret < 0)
{
  byte[] errbuf = new byte[1024];
  av_strerror(ret, errbuf, 1024);
  string error = Encoding.UTF8.GetString(errbuf);
  _logger.LogError($"FFmpeg error: {error}");
}
```

### Timeout Protection

Liquid rescaling operations have configurable timeout:

```csharp
ProcessingSettings.ProcessingTimeoutSeconds (default: 30)
```

**Why:** Prevents hanging on very large images or complex seam patterns

### Validation

Command-line validation ensures:

- Mutually exclusive options (width/height vs percent)
- Required combinations (gradual scaling + duration for images)
- Valid file extensions (video output = .mp4/.mkv only)
- File/directory existence
