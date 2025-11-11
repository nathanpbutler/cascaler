using FFmpeg.AutoGen;
using ImageMagick;
using nathanbutlerDEV.cascaler.Infrastructure;

namespace nathanbutlerDEV.cascaler.Utilities;

/// <summary>
/// Handles pixel format conversion between FFmpeg formats and MagickImage.
/// Supports color-space-aware conversion for proper HDR/wide gamut handling.
/// </summary>
public unsafe class PixelFormatConverter : IDisposable
{
    private readonly SwsContext* _swsContext;
    private readonly int _width;
    private readonly int _height;
    private readonly AVPixelFormat _sourceFormat;
    private readonly AVPixelFormat _destinationFormat;
    private bool _disposed;

    public PixelFormatConverter(
        int width,
        int height,
        AVPixelFormat sourceFormat,
        AVPixelFormat destinationFormat,
        ColorPrimaries? sourcePrimaries = null,
        YuvColorSpace? sourceColorSpace = null,
        ColorRange? sourceColorRange = null,
        ColorPrimaries? destPrimaries = null,
        YuvColorSpace? destColorSpace = null,
        ColorRange? destColorRange = null)
    {
        _width = width;
        _height = height;
        _sourceFormat = sourceFormat;
        _destinationFormat = destinationFormat;

        // Use better quality flags for color conversion
        var flags = ffmpeg.SWS_BILINEAR | ffmpeg.SWS_ACCURATE_RND | ffmpeg.SWS_BITEXACT | ffmpeg.SWS_FULL_CHR_H_INT;

        // Create scaling/conversion context
        _swsContext = ffmpeg.sws_getContext(
            width, height, sourceFormat,
            width, height, destinationFormat,
            flags,
            null, null, null);

        if (_swsContext == null)
            throw new InvalidOperationException("Could not create SwsContext for pixel format conversion");

        // Configure color space conversion if metadata provided
        if (sourcePrimaries.HasValue || sourceColorSpace.HasValue || destPrimaries.HasValue || destColorSpace.HasValue)
        {
            SetColorspaceDetails(
                sourceColorSpace ?? YuvColorSpace.Unspecified,
                sourceColorRange ?? ColorRange.Limited,
                destColorSpace ?? YuvColorSpace.Unspecified,
                destColorRange ?? ColorRange.Limited);
        }
    }

    /// <summary>
    /// Sets color space conversion details for accurate color transformation.
    /// This is critical for HDR/wide gamut content to prevent washed out colors.
    /// </summary>
    private void SetColorspaceDetails(
        YuvColorSpace sourceColorSpace,
        ColorRange sourceColorRange,
        YuvColorSpace destColorSpace,
        ColorRange destColorRange)
    {
        // Get color matrices for source and destination
        var srcMatrixPtr = ffmpeg.sws_getCoefficients((int)sourceColorSpace);
        var dstMatrixPtr = ffmpeg.sws_getCoefficients((int)destColorSpace);

        if (srcMatrixPtr == null || dstMatrixPtr == null)
            return; // Skip if matrices not available

        // Set color space details
        // Range: 0 = full range (0-255), 1 = limited range (16-235)
        var srcRange = sourceColorRange == ColorRange.Full ? 1 : 0;
        var dstRange = destColorRange == ColorRange.Full ? 1 : 0;

        // Copy matrix pointers to fixed arrays for FFmpeg API
        var srcMatrix = new int_array4();
        var dstMatrix = new int_array4();
        for (uint i = 0; i < 4; i++)
        {
            srcMatrix[i] = srcMatrixPtr[i];
            dstMatrix[i] = dstMatrixPtr[i];
        }

        var result = ffmpeg.sws_setColorspaceDetails(
            _swsContext,
            srcMatrix, srcRange,
            dstMatrix, dstRange,
            0, 1 << 16, 1 << 16); // brightness, contrast, saturation (default values)

        if (result < 0)
        {
            // Non-fatal - continue without color space conversion
            // This can happen with some pixel format combinations
        }
    }

    /// <summary>
    /// Converts an AVFrame from source format to destination format.
    /// </summary>
    public AVFrame* Convert(AVFrame* sourceFrame)
    {
        var destinationFrame = ffmpeg.av_frame_alloc();
        destinationFrame->format = (int)_destinationFormat;
        destinationFrame->width = _width;
        destinationFrame->height = _height;
        destinationFrame->pts = sourceFrame->pts;

        if (ffmpeg.av_frame_get_buffer(destinationFrame, 0) < 0)
        {
            var temp = destinationFrame;
            ffmpeg.av_frame_free(&temp);
            throw new InvalidOperationException("Could not allocate destination frame buffer");
        }

        // Perform conversion
        ffmpeg.sws_scale(
            _swsContext,
            sourceFrame->data,
            sourceFrame->linesize,
            0,
            _height,
            destinationFrame->data,
            destinationFrame->linesize);

        return destinationFrame;
    }

    /// <summary>
    /// Converts a MagickImage to an AVFrame in the destination format.
    /// Supports both RGB24 (8-bit) and RGB48 (16-bit for HDR).
    /// </summary>
    public AVFrame* ConvertFromMagickImage(MagickImage image, bool use16Bit = false)
    {
        if (image.Width != _width || image.Height != _height)
            throw new ArgumentException($"Image dimensions {image.Width}x{image.Height} do not match converter dimensions {_width}x{_height}");

        AVPixelFormat sourcePixelFormat;
        byte[] rgbData;
        int bytesPerPixel;

        if (use16Bit)
        {
            // Get RGB48LE data (16-bit per channel) for HDR
            var pixels = image.GetPixels();
            var shortArray = pixels.ToShortArray(PixelMapping.RGB) ?? Array.Empty<ushort>();
            rgbData = new byte[shortArray.Length * 2];
            Buffer.BlockCopy(shortArray, 0, rgbData, 0, rgbData.Length);
            sourcePixelFormat = AVPixelFormat.AV_PIX_FMT_RGB48LE;
            bytesPerPixel = 6; // 16 bits * 3 channels = 6 bytes
        }
        else
        {
            // Get RGB24 data (8-bit per channel) for SDR
            var pixels = image.GetPixels();
            rgbData = pixels.ToByteArray(PixelMapping.RGB) ?? Array.Empty<byte>();
            sourcePixelFormat = AVPixelFormat.AV_PIX_FMT_RGB24;
            bytesPerPixel = 3;
        }

        // Create source frame
        var sourceFrame = ffmpeg.av_frame_alloc();
        sourceFrame->format = (int)sourcePixelFormat;
        sourceFrame->width = _width;
        sourceFrame->height = _height;

        if (ffmpeg.av_frame_get_buffer(sourceFrame, 0) < 0)
        {
            var temp = sourceFrame;
            ffmpeg.av_frame_free(&temp);
            throw new InvalidOperationException("Could not allocate source frame buffer");
        }

        // Copy RGB data to frame
        fixed (byte* pRgbData = rgbData)
        {
            var srcLinesize = _width * bytesPerPixel;
            var src = pRgbData;
            var dst = sourceFrame->data[0];

            for (var y = 0; y < _height; y++)
            {
                Buffer.MemoryCopy(src, dst, srcLinesize, srcLinesize);
                src += srcLinesize;
                dst += sourceFrame->linesize[0];
            }
        }

        // Convert to destination format
        var destinationFrame = Convert(sourceFrame);

        // Free source frame
        var tempSource = sourceFrame;
        ffmpeg.av_frame_free(&tempSource);

        return destinationFrame;
    }

    /// <summary>
    /// Converts an AVFrame to a MagickImage.
    /// Supports both RGB24 (8-bit) and RGB48 (16-bit for HDR).
    /// </summary>
    public MagickImage ConvertToMagickImage(AVFrame* frame, bool use16Bit = false)
    {
        var targetPixelFormat = use16Bit ? AVPixelFormat.AV_PIX_FMT_RGB48LE : AVPixelFormat.AV_PIX_FMT_RGB24;
        AVFrame* rgbFrame;
        var needsFree = false;

        // If frame is not in target RGB format, convert it
        if ((AVPixelFormat)frame->format != targetPixelFormat)
        {
            // Create temporary converter if needed
            using var tempConverter = new PixelFormatConverter(
                frame->width,
                frame->height,
                (AVPixelFormat)frame->format,
                targetPixelFormat);

            rgbFrame = tempConverter.Convert(frame);
            needsFree = true;
        }
        else
        {
            rgbFrame = frame;
        }

        try
        {
            if (use16Bit)
            {
                // Extract RGB48 data (16-bit per channel)
                var dataSize = _width * _height * 6; // 6 bytes per pixel (3 channels * 2 bytes)
                var rgb48Data = new byte[dataSize];

                fixed (byte* pDst = rgb48Data)
                {
                    var dst = pDst;
                    var src = rgbFrame->data[0];
                    var linesize = _width * 6;

                    for (var y = 0; y < _height; y++)
                    {
                        Buffer.MemoryCopy(src, dst, linesize, linesize);
                        src += rgbFrame->linesize[0];
                        dst += linesize;
                    }
                }

                // Convert byte array to ushort array for MagickImage
                var rgb48Shorts = new ushort[_width * _height * 3];
                Buffer.BlockCopy(rgb48Data, 0, rgb48Shorts, 0, rgb48Data.Length);

                // Create MagickImage from RGB48 data
                var image = new MagickImage(MagickColors.Transparent, (uint)_width, (uint)_height);
                image.Depth = 16; // Set to 16-bit depth

                // Convert ushort[] back to byte[] for ReadPixels
                var rgb48Bytes = new byte[rgb48Shorts.Length * 2];
                Buffer.BlockCopy(rgb48Shorts, 0, rgb48Bytes, 0, rgb48Bytes.Length);

                var settings = new PixelReadSettings((uint)_width, (uint)_height, StorageType.Short, PixelMapping.RGB);
                image.ReadPixels(rgb48Bytes, settings);

                return image;
            }
            else
            {
                // Extract RGB24 data (8-bit per channel)
                var dataSize = _width * _height * 3;
                var rgb24Data = new byte[dataSize];

                fixed (byte* pDst = rgb24Data)
                {
                    var dst = pDst;
                    var src = rgbFrame->data[0];
                    var linesize = _width * 3;

                    for (var y = 0; y < _height; y++)
                    {
                        Buffer.MemoryCopy(src, dst, linesize, linesize);
                        src += rgbFrame->linesize[0];
                        dst += linesize;
                    }
                }

                // Create MagickImage from RGB24 data
                var image = new MagickImage(MagickColors.Transparent, (uint)_width, (uint)_height);
                var settings = new PixelReadSettings((uint)_width, (uint)_height, StorageType.Char, PixelMapping.RGB);
                image.ReadPixels(rgb24Data, settings);

                return image;
            }
        }
        finally
        {
            if (needsFree)
            {
                var temp = rgbFrame;
                ffmpeg.av_frame_free(&temp);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_swsContext != null)
        {
            ffmpeg.sws_freeContext(_swsContext);
        }

        _disposed = true;
    }
}
