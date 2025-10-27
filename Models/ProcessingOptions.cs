namespace nathanbutlerDEV.cascaler.Models;

/// <summary>
/// Defines the type of media processing operation.
/// </summary>
public enum ProcessingMode
{
    /// <summary>Processing a single image file.</summary>
    SingleImage,

    /// <summary>Processing multiple images from a folder.</summary>
    ImageBatch,

    /// <summary>Processing a video file (extracting and processing frames).</summary>
    Video
}

/// <summary>
/// Encapsulates all processing options from command-line arguments.
/// </summary>
public class ProcessingOptions
{
    // Input/Output
    public string InputPath { get; set; } = string.Empty;
    public string? OutputPath { get; set; }

    // Target dimensions (end dimensions for gradual scaling)
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Percent { get; set; }

    // Gradual scaling start dimensions
    public int? StartWidth { get; set; }
    public int? StartHeight { get; set; }
    public int? StartPercent { get; set; }

    // Video trimming and sequence generation
    public double? Start { get; set; }
    public double? End { get; set; }
    public double? Duration { get; set; }

    // Output format and frame rate
    public string? Format { get; set; }
    public int Fps { get; set; } = 25;

    // Seam carving parameters
    public double DeltaX { get; set; } = 1.0;
    public double Rigidity { get; set; } = 1.0;

    // Processing configuration
    public int MaxThreads { get; set; } = 16;
    public bool ShowProgress { get; set; } = true;
    public ProcessingMode Mode { get; set; } = ProcessingMode.ImageBatch;
    public bool ScaleBack { get; set; } = false;

    // Video encoding configuration
    public int? CRF { get; set; }
    public string? Preset { get; set; }
    public string? Codec { get; set; }
    public bool Vibrato { get; set; }

    // Computed properties

    /// <summary>
    /// Indicates whether gradual scaling is enabled (start dimensions differ from target dimensions).
    /// </summary>
    public bool IsGradualScaling
    {
        get
        {
            // Check if start dimensions are specified and differ from target
            // Note: StartPercent defaults to 100, Percent defaults to 50
            if (StartPercent.HasValue && StartPercent.Value != Percent)
                return true;

            if (StartWidth.HasValue && Width.HasValue && StartWidth.Value != Width.Value)
                return true;

            if (StartHeight.HasValue && Height.HasValue && StartHeight.Value != Height.Value)
                return true;

            return false;
        }
    }

    /// <summary>
    /// Indicates whether image-to-sequence conversion is requested (single image with duration).
    /// </summary>
    public bool IsImageSequence => Mode == ProcessingMode.SingleImage && Duration.HasValue;

    /// <summary>
    /// Indicates whether video trimming is requested.
    /// </summary>
    public bool HasVideoTrim => Start.HasValue || End.HasValue || (Duration.HasValue && Mode == ProcessingMode.Video);

    /// <summary>
    /// Indicates whether output should be a video file (based on output path extension).
    /// </summary>
    public bool IsVideoOutput
    {
        get
        {
            if (string.IsNullOrEmpty(OutputPath))
                return false;

            var extension = Path.GetExtension(OutputPath);
            return Infrastructure.Constants.SupportedVideoOutputExtensions.Contains(extension);
        }
    }
}
