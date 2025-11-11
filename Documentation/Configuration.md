# Configuration Guide

cascaler supports persistent configuration to customize defaults without specifying command-line arguments every time.

## Configuration File Location

The application reads configuration from a user-specific config file:

- **Unix/macOS/Linux**: `~/.config/cascaler/appsettings.json`
- **Windows**: `%APPDATA%\cascaler\appsettings.json`

## Configuration Priority

Settings are applied in the following order (later overrides earlier):

1. **Embedded defaults** (built into the executable)
2. **User configuration file** (persistent customization)
3. **Command-line arguments** (highest priority, per-execution overrides)

## Configuration Management Commands

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

## Configuration File Structure

### Example Configuration

```json
{
  "FFmpeg": {
    "LibraryPath": "/opt/homebrew/opt/ffmpeg@7/lib",
    "EnableAutoDetection": true
  },
  "Processing": {
    "MaxImageThreads": 32,
    "MaxVideoThreads": 16,
    "ProcessingTimeoutSeconds": 60,
    "DefaultScalePercent": 75,
    "DefaultFps": 30,
    "DefaultVideoFrameFormat": "png",
    "DefaultImageOutputFormat": "",
    "DefaultDeltaX": 1.0,
    "DefaultRigidity": 1.0,
    "DefaultScaleBack": false,
    "DefaultVibrato": false
  },
  "VideoEncoding": {
    "DefaultCRF": 20,
    "DefaultPreset": "medium",
    "DefaultPixelFormat": "yuv420p",
    "DefaultCodec": "libx264"
  },
  "Output": {
    "Suffix": "-scaled",
    "ProgressCharacter": "─",
    "ShowEstimatedDuration": true
  }
}
```

## Configuration Sections

### FFmpeg Section

Controls FFmpeg library loading and path detection.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `LibraryPath` | string | `""` (empty) | Path to FFmpeg libraries directory |
| `EnableAutoDetection` | bool | `true` | Enable automatic FFmpeg path detection |

**Behavior:**

- When `LibraryPath` is empty or invalid and `EnableAutoDetection` is `true`: Automatically searches for FFmpeg in common locations
- When `LibraryPath` is valid: Uses the specified path
- When `EnableAutoDetection` is `false`: Only uses `LibraryPath`, fails if invalid

**Recommendation:** Keep `EnableAutoDetection` as `true` for fallback behavior across different machines.

**FFmpeg Search Paths:**

macOS:

- `/opt/homebrew/opt/ffmpeg@7/lib`
- `/usr/local/opt/ffmpeg@7/lib`
- `/opt/homebrew/lib`
- `/usr/local/lib`

Linux:

- `/usr/lib/x86_64-linux-gnu`
- `/usr/local/lib`
- `/usr/lib`

Windows:

- `C:\Program Files\ffmpeg\lib`
- `C:\ffmpeg\lib`
- `bin\Debug\net10.0\runtimes\win-x64\native\`

**Performance Tip:** Configuring `LibraryPath` eliminates runtime FFmpeg detection, improving startup time.

### Processing Section

Controls default processing behavior and performance settings.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `MaxImageThreads` | int | `16` | Maximum parallel threads for image processing |
| `MaxVideoThreads` | int | `8` | Maximum parallel threads for video frame processing |
| `ProcessingTimeoutSeconds` | int | `30` | Timeout for liquid rescale operations |
| `DefaultScalePercent` | int | `50` | Default scaling percentage (0-100) |
| `DefaultFps` | int | `25` | Default frame rate for sequences |
| `DefaultVideoFrameFormat` | string | `"png"` | Default format for video frame extraction |
| `DefaultImageOutputFormat` | string | `""` | Default output format for images (empty = preserve input format) |
| `DefaultDeltaX` | double | `1.0` | Seam transversal step (0=straight, 1=curved) |
| `DefaultRigidity` | double | `1.0` | Bias for non-straight seams (0-10) |
| `DefaultScaleBack` | bool | `false` | Scale processed frames back to original 100% dimensions |
| `DefaultVibrato` | bool | `false` | Apply vibrato/tremolo audio effects by default |

**Performance Notes:**

- Increase `MaxImageThreads` for faster batch processing (if you have more CPU cores)
- Increase `MaxVideoThreads` for faster video processing (uses more memory)
- Lower `DefaultDeltaX` and `DefaultRigidity` for faster processing (straighter seams)
- Increase `ProcessingTimeoutSeconds` for very large images or complex processing

### VideoEncoding Section

Controls video encoding quality and format settings.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `DefaultCRF` | int | `23` | H.264 Constant Rate Factor (0-51, lower = better quality) |
| `DefaultPreset` | string | `"medium"` | Encoding speed preset |
| `DefaultPixelFormat` | string | `"yuv420p"` | Pixel format for video compatibility |
| `DefaultCodec` | string | `"libx264"` | Video codec to use |

**CRF Values:**

- `0`: Lossless (huge file size)
- `18`: Visually lossless (very high quality)
- `23`: Default (good quality, reasonable size)
- `28`: Medium quality
- `35`: Low quality
- `51`: Worst quality

**Presets:**

- `ultrafast`: Fastest encoding, largest file size
- `superfast`, `veryfast`, `faster`, `fast`
- `medium`: Default (balanced)
- `slow`, `slower`: Better compression, slower encoding
- `veryslow`: Best compression, very slow encoding

**Codecs:**

- `libx264`: H.264 (best compatibility)
- `libx265`: H.265/HEVC (better compression, less compatible)

**Pixel Formats:**

- `yuv420p`: Standard (best compatibility)
- `yuv422p`: Higher quality (larger files, less compatible)
- `yuv444p`: Highest quality (largest files, least compatible)

### Output Section

Controls output naming and progress display.

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Suffix` | string | `"-cas"` | Suffix appended to output files/folders |
| `ProgressCharacter` | string | `"─"` | Character used for progress bar |
| `ShowEstimatedDuration` | bool | `true` | Show estimated time remaining in progress bar |

**Examples:**

- `Suffix = "-scaled"`: `input.jpg` → `input-scaled.jpg`
- `Suffix = "_processed"`: `video.mp4` → `video_processed.mp4`
- `ProgressCharacter = "█"`: Changes progress bar appearance

## Configuration Workflow

### Initial Setup

1. **Create configuration file:**

   ```bash
   cascaler config init --detect-ffmpeg
   ```

2. **Verify configuration:**

   ```bash
   cascaler config show
   ```

3. **Edit configuration file:**
   - Open the file shown by `cascaler config path`
   - Modify settings as needed
   - Save the file

4. **Test configuration:**

   ```bash
   cascaler input.jpg  # Uses your configured defaults
   ```

### Per-Project Configuration

You can create project-specific configurations:

```bash
# Export current config for a project
cascaler config export ~/projects/myproject/cascaler-config.json

# Note: Per-project configs are not automatically loaded
# You must use environment variables or specify paths if implementing custom config loading
```

### Resetting Configuration

To reset to defaults, simply delete the user configuration file:

```bash
# Show config file location
cascaler config path

# Delete the file (varies by OS)
rm ~/.config/cascaler/appsettings.json        # Unix/macOS/Linux
del %APPDATA%\cascaler\appsettings.json       # Windows
```

## Common Configuration Scenarios

### High-Quality Video Processing

```json
{
  "Processing": {
    "DefaultScalePercent": 75,
    "DefaultFps": 60
  },
  "VideoEncoding": {
    "DefaultCRF": 18,
    "DefaultPreset": "slow"
  }
}
```

### Fast Batch Processing

```json
{
  "Processing": {
    "MaxImageThreads": 32,
    "DefaultDeltaX": 0.5,
    "DefaultRigidity": 0.5
  }
}
```

### Web-Optimized Output

```json
{
  "Processing": {
    "DefaultScalePercent": 75,
    "DefaultImageOutputFormat": "jpg"
  },
  "VideoEncoding": {
    "DefaultCRF": 28,
    "DefaultPreset": "fast"
  }
}
```

### Creative Effects

```json
{
  "Processing": {
    "DefaultScaleBack": true,
    "DefaultVibrato": true,
    "DefaultFps": 30
  },
  "Output": {
    "Suffix": "-effect"
  }
}
```

## Troubleshooting

### FFmpeg Not Found

If you see "FFmpeg libraries not found" errors:

1. **Check FFmpeg installation:**

   ```bash
   ffmpeg -version
   ```

2. **Find FFmpeg library path:**
   - macOS: `brew --prefix ffmpeg@7`
   - Linux: `ldconfig -p | grep libavcodec`
   - Windows: Check installation directory

3. **Update configuration:**

   ```bash
   cascaler config init --detect-ffmpeg
   ```

   Or manually edit the config file with the correct `LibraryPath`.

### Configuration Not Loading

1. **Verify file location:**

   ```bash
   cascaler config path
   ```

2. **Check JSON syntax:**
   - Use a JSON validator (e.g., jsonlint.com)
   - Look for missing commas, brackets, or quotes

3. **View effective configuration:**

   ```bash
   cascaler config show
   ```

   This shows what settings are actually being used.

### Settings Not Taking Effect

Remember the priority order:

1. Embedded defaults
2. User configuration file
3. **Command-line arguments** (always win)

If a command-line argument is specified, it overrides the configuration file setting.

## Advanced Topics

### Environment Variables

cascaler uses the following environment variables:

- `FFMPEG_PATH`: Overrides FFmpeg library path detection
- `APPDATA` (Windows): Base directory for configuration
- `HOME` (Unix): Base directory for configuration

### Logging Configuration

Logs are automatically written to:

- Unix/macOS/Linux: `~/.config/cascaler/logs/cascaler-YYYYMMDD.log`
- Windows: `%APPDATA%\cascaler\logs\cascaler-YYYYMMDD.log`

Log files are retained for 7 days and automatically cleaned up.

To disable progress bar and see more console output:

```bash
cascaler input.jpg --no-progress
```

### Configuration Schema

The configuration file follows standard .NET configuration format with sections, properties, and JSON data types:

- **Strings**: Enclosed in quotes (`"value"`)
- **Numbers**: No quotes (`42`, `3.14`)
- **Booleans**: `true` or `false` (no quotes)
- **Objects**: Nested `{ }` blocks

Invalid JSON will cause the configuration to be ignored and defaults will be used.
