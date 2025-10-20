using ImageMagick;

namespace cascaler.Services.Interfaces;

/// <summary>
/// Service for loading, processing, and saving images with content-aware liquid rescaling.
/// </summary>
public interface IImageProcessingService
{
    /// <summary>
    /// Checks if a file is a supported image format.
    /// </summary>
    bool IsImageFile(string filePath);

    /// <summary>
    /// Checks if a file is a supported video format.
    /// </summary>
    bool IsVideoFile(string filePath);

    /// <summary>
    /// Checks if a file is a supported media format (image or video).
    /// </summary>
    bool IsMediaFile(string filePath);

    /// <summary>
    /// Loads an image from the specified file path.
    /// </summary>
    Task<MagickImage?> LoadImageAsync(string filePath);

    /// <summary>
    /// Processes an image with liquid rescaling using the specified parameters.
    /// </summary>
    Task<MagickImage?> ProcessImageAsync(
        MagickImage originalImage,
        int? targetWidth,
        int? targetHeight,
        int? scalePercent,
        double deltaX = 1.0,
        double rigidity = 0.0);

    /// <summary>
    /// Saves a processed image to the specified output path.
    /// </summary>
    Task<bool> SaveImageAsync(MagickImage image, string outputPath);
}
