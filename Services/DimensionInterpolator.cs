using cascaler.Models;
using cascaler.Services.Interfaces;

namespace cascaler.Services;

/// <summary>
/// Calculates interpolated dimensions for gradual scaling across multiple frames.
/// Uses linear interpolation to smoothly transition from start to end dimensions.
/// </summary>
public class DimensionInterpolator : IDimensionInterpolator
{
    public (int width, int height) CalculateFrameDimensions(
        int frameIndex,
        int totalFrames,
        ProcessingOptions options)
    {
        if (totalFrames <= 1)
            throw new ArgumentException("Total frames must be greater than 1 for interpolation", nameof(totalFrames));

        if (frameIndex < 0 || frameIndex >= totalFrames)
            throw new ArgumentOutOfRangeException(nameof(frameIndex), "Frame index must be within range [0, totalFrames)");

        // Get original dimensions (we'll need these if using percentages)
        // For now, we'll handle absolute dimensions and percentages separately

        // Calculate interpolation factor (0.0 at start, 1.0 at end)
        double t = totalFrames > 1 ? (double)frameIndex / (totalFrames - 1) : 0.0;

        // Handle percentage-based scaling
        if (options.StartPercent.HasValue && options.Percent.HasValue)
        {
            // Can't interpolate percentages without original dimensions
            // This should be handled by the caller passing original dimensions
            throw new InvalidOperationException(
                "Percentage-based gradual scaling requires original dimensions. " +
                "Use GetStartDimensions and GetEndDimensions to convert to absolute dimensions first.");
        }

        // Handle absolute dimensions
        if (options.StartWidth.HasValue && options.StartHeight.HasValue &&
            options.Width.HasValue && options.Height.HasValue)
        {
            int width = (int)Math.Round(
                options.StartWidth.Value + (options.Width.Value - options.StartWidth.Value) * t);
            int height = (int)Math.Round(
                options.StartHeight.Value + (options.Height.Value - options.StartHeight.Value) * t);

            return (width, height);
        }

        throw new InvalidOperationException(
            "Invalid dimension configuration. Both start and end dimensions must be specified as absolute values.");
    }

    public (int startWidth, int startHeight) GetStartDimensions(
        int originalWidth,
        int originalHeight,
        ProcessingOptions options)
    {
        // If explicit start dimensions provided, use them
        if (options.StartWidth.HasValue && options.StartHeight.HasValue)
        {
            return (options.StartWidth.Value, options.StartHeight.Value);
        }

        // If start percent provided, calculate from original dimensions
        if (options.StartPercent.HasValue)
        {
            int width = (int)Math.Round(originalWidth * options.StartPercent.Value / 100.0);
            int height = (int)Math.Round(originalHeight * options.StartPercent.Value / 100.0);
            return (width, height);
        }

        // Default to original dimensions (100%)
        return (originalWidth, originalHeight);
    }

    public (int endWidth, int endHeight) GetEndDimensions(
        int originalWidth,
        int originalHeight,
        ProcessingOptions options)
    {
        // If explicit end dimensions provided, use them
        if (options.Width.HasValue && options.Height.HasValue)
        {
            return (options.Width.Value, options.Height.Value);
        }

        // If end percent provided, calculate from original dimensions
        if (options.Percent.HasValue)
        {
            int width = (int)Math.Round(originalWidth * options.Percent.Value / 100.0);
            int height = (int)Math.Round(originalHeight * options.Percent.Value / 100.0);
            return (width, height);
        }

        // Default to 50% (from Constants.DefaultScalePercent)
        int defaultWidth = (int)Math.Round(originalWidth * 0.5);
        int defaultHeight = (int)Math.Round(originalHeight * 0.5);
        return (defaultWidth, defaultHeight);
    }
}
