# Command-Line Reference

Complete reference for all cascaler command-line options.

## Basic Syntax

```bash
cascaler <input> [options]
```

Where `<input>` can be:

- A single image file
- A single video file
- A directory containing images

## Scaling Options

### Target Dimensions

| Option      | Alias | Description              | Default |
|-------------|-------|--------------------------|---------|
| `--width`   | `-w`  | Target width in pixels   | -       |
| `--height`  | `-h`  | Target height in pixels  | -       |
| `--percent` | `-p`  | Scale percentage (0-100) | 50      |

**Examples:**

```bash
cascaler input.jpg -w 800 -h 600      # Scale to specific dimensions
cascaler input.jpg -p 75              # Scale to 75% of original size
```

**Note:** Choose either width/height OR percent, not both.

### Gradual Scaling

| Option            | Alias | Description                             | Default      |
|-------------------|-------|-----------------------------------------|--------------|
| `--start-percent` | `-sp` | Starting percentage for gradual scaling | same as `-p` |
| `--start-width`   | `-sw` | Starting width for gradual scaling      | -            |
| `--start-height`  | `-sh` | Starting height for gradual scaling     | -            |

**Examples:**

```bash
# Gradual scaling from 100% to 50% over video duration
cascaler input.mp4 -o output.mp4 -sp 100 -p 50

# Batch images with gradual scaling (75% → 25%)
cascaler /path/to/images -sp 75 -p 25
```

**Note:** Choose either start-width/height OR start-percent, not both.

## Advanced Scaling Options

| Option         | Alias | Description                                             | Default |
|----------------|-------|---------------------------------------------------------|---------|
| `--deltaX`    | `-d`  | Seam transversal step (0=straight, 1=curved)            | 1.0     |
| `--rigidity`   | `-r`  | Bias for non-straight seams (0-10)                      | 1.0     |
| `--scale-back` | -     | Scale processed frames back to original 100% dimensions | false   |

**Examples:**

```bash
# Straighter seams (faster processing)
cascaler input.jpg -d 0.5 -r 0.5

# Apply liquid rescaling effect, then scale back to original size
cascaler input.jpg --scale-back -p 50
```

## Processing Options

| Option          | Alias | Description                               | Default               |
|-----------------|-------|-------------------------------------------|-----------------------|
| `--threads`     | `-t`  | Number of parallel processing threads     | 16                    |
| `--output`      | `-o`  | Output path (file or directory)           | input + `-cas` suffix |
| `--no-progress` | -     | Disable progress bar and progress updates | false                 |

**Examples:**

```bash
# Use 32 threads for faster processing
cascaler input.jpg -t 32

# Custom output location
cascaler input.jpg -o /path/to/output.jpg

# Disable progress bar
cascaler input.jpg --no-progress
```

## Video & Sequence Options

### Frame Control

| Option     | Alias | Description                               | Default      |
|------------|-------|-------------------------------------------|--------------|
| `--format` | `-f`  | Output image format (png, jpg, bmp, tiff) | input format |
| `--fps`    | -     | Frame rate for sequences                  | 25           |

**Examples:**

```bash
# Output frames as JPEG
cascaler input.mp4 -f jpg

# Generate 60fps sequence
cascaler input.jpg --duration 3 --fps 60
```

### Trimming

| Option       | Alias | Description           | Default |
|--------------|-------|-----------------------|---------|
| `--start`    | -     | Start time in seconds | -       |
| `--end`      | -     | End time in seconds   | -       |
| `--duration` | -     | Duration in seconds   | -       |

**Examples:**

```bash
# Trim from 10s to 20s
cascaler input.mp4 --start 10 --end 20

# Trim 5 seconds starting at 10s
cascaler input.mp4 --start 10 --duration 5

# Generate 3-second sequence from image
cascaler input.jpg --duration 3 -o output.mp4
```

**Note:** Choose either `--end` OR `--duration`, not both.

### Audio Effects

| Option      | Alias | Description                             | Default |
|-------------|-------|-----------------------------------------|---------|
| `--vibrato` | -     | Apply vibrato and tremolo audio effects | false   |

**Example:**

```bash
cascaler input.mp4 -o output.mp4 --vibrato
```

## Configuration Commands

Manage persistent configuration settings:

```bash
cascaler config show                             # Show current effective configuration
cascaler config path                             # Show config file path
cascaler config init [--detect-ffmpeg]          # Create user config file
cascaler config export <file> [--detect-ffmpeg] # Export config to file
```

**Example:**

```bash
# Initialize config with automatic FFmpeg path detection
cascaler config init --detect-ffmpeg
```

## Processing Modes

cascaler automatically detects the processing mode based on input and options:

### Single Image

```bash
cascaler input.jpg -p 75
```

Applies liquid rescaling to a single image.

### Image Sequence (Image to Video/Frames)

```bash
cascaler input.jpg --duration 3 -sp 100 -p 50
```

Generates frames from a static image with gradual scaling.

### Video Processing

```bash
cascaler input.mp4 -o output.mp4 -p 75
```

Processes video frames with liquid rescaling, preserves audio.

### Batch Processing

```bash
cascaler /path/to/images -p 50
```

Processes multiple images in parallel.

### Directory to Video

```bash
cascaler /path/to/images -o output.mp4 -sp 75 -p 25
```

Converts directory of images to video with gradual scaling.

## Validation Rules

- **Scaling:** Choose width/height OR percent (not both)
- **Gradual scaling:** Choose start-width/height OR start-percent (not both)
- **Trimming:** Choose `--end` OR `--duration` (not both)
- **Image sequences:** Require `--duration` when using gradual scaling on single image
- **Batch mode:** Cannot use duration/start/end parameters
- **Video output:** Only .mp4 or .mkv extensions are allowed for video output (for now)

## Supported Formats

### Input Images

`.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.tif`, `.webp`, `.ico`

### Input Videos

`.mp4`, `.avi`, `.mov`, `.mkv`, `.webm`, `.wmv`, `.flv`, `.m4v`

### Output Videos

`.mp4`, `.mkv`

### Frame Output Formats

`png`, `jpg`, `bmp`, `tiff` (via `--format`)

## Common Usage Patterns

### Image Rescaling

```bash
# Single image at 75%
cascaler input.jpg -p 75

# Single image to specific dimensions
cascaler input.jpg -w 1920 -h 1080

# Process all images in directory
cascaler /path/to/images -p 50
```

### Video Rescaling

```bash
# Process entire video
cascaler input.mp4 -o output.mp4 -p 75

# Process video segment
cascaler input.mp4 -o output.mp4 --start 10 --duration 5 -p 50

# Extract processed frames
cascaler input.mp4 -f png -p 75
```

### Creative Effects

```bash
# Gradual scaling effect on video
cascaler input.mp4 -o output.mp4 -sp 100 -p 50

# Image to video with gradual scaling
cascaler input.jpg -o output.mp4 --duration 3 -sp 100 -p 50

# Apply effect and scale back to original size
cascaler input.jpg --scale-back -p 50

# Directory to video with gradual scaling
cascaler /path/to/images -o output.mp4 -sp 75 -p 25

# Video with audio effects
cascaler input.mp4 -o output.mp4 --vibrato -p 75
```

### Performance Tuning

```bash
# Use more threads for faster processing
cascaler input.jpg -t 32 -p 50

# Faster seam finding (straighter seams)
cascaler input.jpg -d 0.5 -r 0.5
```
