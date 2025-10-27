using FFmpeg.AutoGen;
using ImageMagick;

namespace nathanbutlerDEV.cascaler.Utilities;

/// <summary>
/// Handles pixel format conversion between FFmpeg formats and MagickImage.
/// Primarily converts between RGB24 (MagickImage) and YUV420P (H.264 encoder).
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
        AVPixelFormat destinationFormat)
    {
        _width = width;
        _height = height;
        _sourceFormat = sourceFormat;
        _destinationFormat = destinationFormat;

        // Create scaling/conversion context
        _swsContext = ffmpeg.sws_getContext(
            width, height, sourceFormat,
            width, height, destinationFormat,
            ffmpeg.SWS_BILINEAR,
            null, null, null);

        if (_swsContext == null)
            throw new InvalidOperationException("Could not create SwsContext for pixel format conversion");
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
    /// Converts a MagickImage (RGB24) to an AVFrame in the destination format.
    /// </summary>
    public AVFrame* ConvertFromMagickImage(MagickImage image)
    {
        if (image.Width != _width || image.Height != _height)
            throw new ArgumentException($"Image dimensions {image.Width}x{image.Height} do not match converter dimensions {_width}x{_height}");

        // Get RGB24 data from MagickImage
        var pixels = image.GetPixels();
        var rgb24Data = pixels.ToByteArray(PixelMapping.RGB);

        // Create source frame (RGB24)
        var sourceFrame = ffmpeg.av_frame_alloc();
        sourceFrame->format = (int)AVPixelFormat.AV_PIX_FMT_RGB24;
        sourceFrame->width = _width;
        sourceFrame->height = _height;

        if (ffmpeg.av_frame_get_buffer(sourceFrame, 0) < 0)
        {
            var temp = sourceFrame;
            ffmpeg.av_frame_free(&temp);
            throw new InvalidOperationException("Could not allocate source frame buffer");
        }

        // Copy RGB24 data to frame
        fixed (byte* pRgbData = rgb24Data)
        {
            var srcLinesize = _width * 3; // RGB24 = 3 bytes per pixel
            var src = pRgbData;
            var dst = sourceFrame->data[0];

            for (int y = 0; y < _height; y++)
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
    /// Converts an AVFrame to a MagickImage (assumes source is RGB24 compatible or will be converted).
    /// </summary>
    public MagickImage ConvertToMagickImage(AVFrame* frame)
    {
        AVFrame* rgb24Frame;
        bool needsFree = false;

        // If frame is not RGB24, convert it
        if ((AVPixelFormat)frame->format != AVPixelFormat.AV_PIX_FMT_RGB24)
        {
            // Create temporary converter if needed
            using var tempConverter = new PixelFormatConverter(
                frame->width,
                frame->height,
                (AVPixelFormat)frame->format,
                AVPixelFormat.AV_PIX_FMT_RGB24);

            rgb24Frame = tempConverter.Convert(frame);
            needsFree = true;
        }
        else
        {
            rgb24Frame = frame;
        }

        try
        {
            // Extract RGB24 data
            var dataSize = _width * _height * 3;
            var rgb24Data = new byte[dataSize];

            fixed (byte* pDst = rgb24Data)
            {
                var dst = pDst;
                var src = rgb24Frame->data[0];
                var linesize = _width * 3;

                for (int y = 0; y < _height; y++)
                {
                    Buffer.MemoryCopy(src, dst, linesize, linesize);
                    src += rgb24Frame->linesize[0];
                    dst += linesize;
                }
            }

            // Create MagickImage from RGB24 data
            var image = new MagickImage(MagickColors.Transparent, (uint)_width, (uint)_height);
            var settings = new PixelReadSettings((uint)_width, (uint)_height, StorageType.Char, PixelMapping.RGB);
            image.ReadPixels(rgb24Data, settings);

            return image;
        }
        finally
        {
            if (needsFree)
            {
                var temp = rgb24Frame;
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
