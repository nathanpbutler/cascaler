using nathanbutlerDEV.cascaler.Models;

namespace nathanbutlerDEV.cascaler.Services.Interfaces;

/// <summary>
/// Calculates interpolated dimensions for gradual scaling across multiple frames.
/// </summary>
public interface IDimensionInterpolator
{
    /// <summary>
    /// Determines the start dimensions based on options or original dimensions.
    /// </summary>
    /// <param name="originalWidth">Original image/video width</param>
    /// <param name="originalHeight">Original image/video height</param>
    /// <param name="options">Processing options</param>
    /// <returns>Tuple containing start width and height</returns>
    (int startWidth, int startHeight) GetStartDimensions(
        int originalWidth,
        int originalHeight,
        ProcessingOptions options);

    /// <summary>
    /// Determines the end (target) dimensions based on options.
    /// </summary>
    /// <param name="originalWidth">Original image/video width</param>
    /// <param name="originalHeight">Original image/video height</param>
    /// <param name="options">Processing options</param>
    /// <returns>Tuple containing end width and height</returns>
    (int endWidth, int endHeight) GetEndDimensions(
        int originalWidth,
        int originalHeight,
        ProcessingOptions options);
}
