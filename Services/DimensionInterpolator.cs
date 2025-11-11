using nathanbutlerDEV.cascaler.Models;
using nathanbutlerDEV.cascaler.Services.Interfaces;

namespace nathanbutlerDEV.cascaler.Services;

/// <summary>
/// Calculates interpolated dimensions for gradual scaling across multiple frames.
/// Uses linear interpolation to smoothly transition from start to end dimensions.
/// </summary>
public class DimensionInterpolator : IDimensionInterpolator
{
    public (int startWidth, int startHeight) GetStartDimensions(
        int originalWidth,
        int originalHeight,
        ProcessingOptions options)
    {
        // If explicit start dimensions provided, use them
        if (options is { StartWidth: not null, StartHeight: not null })
        {
            return (options.StartWidth.Value, options.StartHeight.Value);
        }

        // If start percent provided, calculate from original dimensions
        if (options.StartPercent.HasValue)
        {
            var width = (int)Math.Round(originalWidth * options.StartPercent.Value / 100.0);
            var height = (int)Math.Round(originalHeight * options.StartPercent.Value / 100.0);
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
        if (options is { Width: not null, Height: not null })
        {
            return (options.Width.Value, options.Height.Value);
        }

        // If end percent provided, calculate from original dimensions
        if (options.Percent.HasValue)
        {
            var width = (int)Math.Round(originalWidth * options.Percent.Value / 100.0);
            var height = (int)Math.Round(originalHeight * options.Percent.Value / 100.0);
            return (width, height);
        }

        // Default to 50% (from Constants.DefaultScalePercent)
        var defaultWidth = (int)Math.Round(originalWidth * 0.5);
        var defaultHeight = (int)Math.Round(originalHeight * 0.5);
        return (defaultWidth, defaultHeight);
    }
}
