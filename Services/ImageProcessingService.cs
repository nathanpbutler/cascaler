using cascaler.Infrastructure;
using cascaler.Services.Interfaces;
using ImageMagick;

namespace cascaler.Services;

/// <summary>
/// Handles all image-related operations including loading, processing with liquid rescaling, and saving.
/// </summary>
public class ImageProcessingService : IImageProcessingService
{
    private readonly ProcessingConfiguration _config;

    public ImageProcessingService(ProcessingConfiguration config)
    {
        _config = config;
    }

    public bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return Constants.SupportedImageExtensions.Contains(extension);
    }

    public bool IsVideoFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return Constants.SupportedVideoExtensions.Contains(extension);
    }

    public bool IsMediaFile(string filePath)
    {
        return IsImageFile(filePath) || IsVideoFile(filePath);
    }

    public async Task<MagickImage?> LoadImageAsync(string filePath)
    {
        if (!File.Exists(filePath) || !IsImageFile(filePath))
            return null;

        try
        {
            return await Task.Run(() => new MagickImage(filePath));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading image {filePath}: {ex.Message}");
            return null;
        }
    }

    public async Task<MagickImage?> ProcessImageAsync(
        MagickImage originalImage,
        int? targetWidth,
        int? targetHeight,
        int? scalePercent,
        double deltaX = 1.0,
        double rigidity = 0.0)
    {
        try
        {
            var processedImage = (MagickImage)originalImage.Clone();

            // Calculate dimensions based on provided parameters
            uint newWidth, newHeight;

            if (targetWidth.HasValue && targetHeight.HasValue)
            {
                newWidth = (uint)targetWidth.Value;
                newHeight = (uint)targetHeight.Value;
            }
            else if (scalePercent.HasValue)
            {
                var scale = scalePercent.Value / 100.0;
                newWidth = (uint)(originalImage.Width * scale);
                newHeight = (uint)(originalImage.Height * scale);
            }
            else
            {
                // Default to 50% if nothing specified
                newWidth = (uint)(originalImage.Width * 0.5);
                newHeight = (uint)(originalImage.Height * 0.5);
            }

            if (newWidth < 1) newWidth = 1;
            if (newHeight < 1) newHeight = 1;

            if (newWidth == originalImage.Width && newHeight == originalImage.Height)
            {
                return processedImage;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(_config.ProcessingTimeoutSeconds));

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        processedImage.LiquidRescale(newWidth, newHeight, deltaX, rigidity);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Liquid rescale failed, falling back to regular resize: {ex.Message}");
                        // Fallback to regular resize
                        var geometry = new MagickGeometry(newWidth, newHeight)
                        {
                            IgnoreAspectRatio = true
                        };
                        processedImage.Resize(geometry);
                    }
                }, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Processing timeout after {_config.ProcessingTimeoutSeconds} seconds, using regular resize");
                // Timeout fallback
                var geometry = new MagickGeometry(newWidth, newHeight)
                {
                    IgnoreAspectRatio = true
                };
                processedImage.Resize(geometry);
            }

            return processedImage;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing image: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> SaveImageAsync(MagickImage image, string outputPath, string? format = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // If format is specified, set the output format and update the path extension
            if (!string.IsNullOrEmpty(format))
            {
                // Map format name to ImageMagick MagickFormat
                var magickFormat = format.ToLowerInvariant() switch
                {
                    "png" => MagickFormat.Png,
                    "jpg" or "jpeg" => MagickFormat.Jpeg,
                    "bmp" => MagickFormat.Bmp,
                    "tiff" or "tif" => MagickFormat.Tiff,
                    _ => throw new ArgumentException($"Unsupported output format: {format}", nameof(format))
                };

                // Update the output path extension to match the format
                if (Constants.FormatExtensions.TryGetValue(format, out var extension))
                {
                    outputPath = Path.ChangeExtension(outputPath, extension);
                }

                image.Format = magickFormat;
            }

            await Task.Run(() => image.Write(outputPath));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving image to {outputPath}: {ex.Message}");
            return false;
        }
    }
}
