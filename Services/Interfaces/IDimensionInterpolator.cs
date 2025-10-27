using nathanbutlerDEV.cascaler.Models;

namespace nathanbutlerDEV.cascaler.Services.Interfaces;

/// <summary>
/// Calculates interpolated dimensions for gradual scaling across multiple frames.
/// </summary>
public interface IDimensionInterpolator
{
    /// <summary>
    /// Calculates the dimensions for a specific frame in a gradual scaling sequence.
    /// </summary>
    /// <param name="frameIndex">Zero-based index of the current frame</param>
    /// <param name="totalFrames">Total number of frames in the sequence</param>
    /// <param name="options">Processing options containing start and end dimensions</param>
    /// <returns>Tuple containing interpolated width and height for the frame</returns>
    (int width, int height) CalculateFrameDimensions(
        int frameIndex,
        int totalFrames,
        ProcessingOptions options);

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
