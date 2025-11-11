using FFmpeg.AutoGen;
using ImageMagick;
using Microsoft.Extensions.Logging;
using nathanbutlerDEV.cascaler.Infrastructure;
using nathanbutlerDEV.cascaler.Utilities;

namespace nathanbutlerDEV.cascaler.Services.FFmpeg;

/// <summary>
/// Encodes video frames to H.264/H.265 format using FFmpeg.AutoGen.
/// </summary>
public unsafe class VideoEncoder : IDisposable
{
    private readonly ILogger<VideoEncoder> _logger;
    private readonly AVCodecContext* _codecContext;
    private readonly AVCodec* _codec;
    private readonly PixelFormatConverter _pixelConverter;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly bool _isHDR;
    private readonly bool _use16BitRgb;
    private long _frameNumber;
    private bool _disposed;

    public int Width => _width;
    public int Height => _height;
    public int Fps => _fps;
    public AVCodecID CodecID => _codec->id;
    public AVRational TimeBase => _codecContext->time_base;
    public AVCodecContext* CodecContext => _codecContext;
    public ColorPrimaries ColorPrimaries { get; }
    public TransferCharacteristic TransferCharacteristic { get; }
    public YuvColorSpace ColorSpace { get; }
    public ColorRange ColorRange { get; }

    public VideoEncoder(
        int width,
        int height,
        int fps,
        AVCodecID codecId,
        int crf,
        string preset,
        ILogger<VideoEncoder> logger,
        ColorPrimaries? colorPrimaries = null,
        TransferCharacteristic? transferCharacteristic = null,
        YuvColorSpace? colorSpace = null,
        ColorRange? colorRange = null,
        int? bitDepth = null,
        string? pixelFormat = null)
    {
        _logger = logger;
        _width = width;
        _height = height;
        _fps = fps;
        _frameNumber = 0;

        // Store color metadata (use provided values or defaults)
        ColorPrimaries = colorPrimaries ?? ColorPrimaries.Unspecified;
        TransferCharacteristic = transferCharacteristic ?? TransferCharacteristic.Unspecified;
        ColorSpace = colorSpace ?? YuvColorSpace.Unspecified;
        ColorRange = colorRange ?? ColorRange.Unspecified;

        // Detect if HDR based on transfer characteristic
        _isHDR = transferCharacteristic is TransferCharacteristic.PQ or TransferCharacteristic.HLG or TransferCharacteristic.SMPTE428;

        // Determine output pixel format with precedence: user-specified → auto-detect
        AVPixelFormat outputPixelFormat;
        var effectiveBitDepth = bitDepth ?? 8;

        if (!string.IsNullOrEmpty(pixelFormat) && TryParsePixelFormat(pixelFormat, out var userPixelFormat))
        {
            // User specified a pixel format (from CLI or config) - use it
            outputPixelFormat = userPixelFormat;
            _use16BitRgb = Is10BitOrHigher(outputPixelFormat);
            _logger.LogInformation("Using user-specified pixel format: {PixelFormat} ({Use16Bit}-bit RGB conversion)",
                pixelFormat, _use16BitRgb ? 16 : 8);
        }
        else if (_isHDR || effectiveBitDepth >= 10)
        {
            // Auto-detect: Use 10-bit format for HDR or high bit depth content
            outputPixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P10LE;
            _use16BitRgb = true; // Use RGB48 for ImageMagick conversion
            _logger.LogInformation("Auto-detected HDR/high bit depth content, using 10-bit YUV420P10LE");
        }
        else
        {
            // Auto-detect: Use 8-bit format for SDR content
            outputPixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;
            _use16BitRgb = false; // Use RGB24 for ImageMagick conversion
        }

        // Find encoder
        _codec = ffmpeg.avcodec_find_encoder(codecId);
        if (_codec == null)
            throw new InvalidOperationException($"Could not find encoder for codec {codecId}");

        // Allocate codec context
        _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
        if (_codecContext == null)
            throw new InvalidOperationException("Could not allocate codec context");

        // Set codec parameters
        _codecContext->width = width;
        _codecContext->height = height;
        _codecContext->time_base = new AVRational { num = 1, den = fps };
        _codecContext->framerate = new AVRational { num = fps, den = 1 };
        _codecContext->pix_fmt = outputPixelFormat;
        _codecContext->gop_size = fps * 2; // Keyframe every 2 seconds
        _codecContext->max_b_frames = 2;

        // Set color metadata in encoder context
        if (ColorPrimaries != ColorPrimaries.Unspecified)
            _codecContext->color_primaries = (AVColorPrimaries)ColorPrimaries;
        if (TransferCharacteristic != TransferCharacteristic.Unspecified)
            _codecContext->color_trc = (AVColorTransferCharacteristic)TransferCharacteristic;
        if (ColorSpace != YuvColorSpace.Unspecified)
            _codecContext->colorspace = (AVColorSpace)ColorSpace;
        if (ColorRange != ColorRange.Unspecified)
            _codecContext->color_range = (AVColorRange)ColorRange;

        // Set CRF (Constant Rate Factor) for quality
        ffmpeg.av_opt_set_int(_codecContext->priv_data, "crf", crf, 0);

        // Set encoding preset
        ffmpeg.av_opt_set(_codecContext->priv_data, "preset", preset, 0);

        // Additional HDR-specific options for libx265
        if (_isHDR && codecId == AVCodecID.AV_CODEC_ID_HEVC)
        {
            ffmpeg.av_opt_set(_codecContext->priv_data, "x265-params", "hdr10=1", 0);
            _logger.LogDebug("HDR10 encoding enabled for HEVC");
        }

        // Open codec
        if (ffmpeg.avcodec_open2(_codecContext, _codec, null) < 0)
            throw new InvalidOperationException("Could not open codec");

        // Create pixel format converter with color space awareness
        var sourcePixelFormat = _use16BitRgb ? AVPixelFormat.AV_PIX_FMT_RGB48LE : AVPixelFormat.AV_PIX_FMT_RGB24;
        _pixelConverter = new PixelFormatConverter(
            width,
            height,
            sourcePixelFormat,
            outputPixelFormat,
            destColorSpace: ColorSpace,
            destColorRange: ColorRange);

        _logger.LogDebug("Video encoder initialized - Codec: {Codec}, Size: {Width}x{Height}, FPS: {FPS}, CRF: {CRF}, Preset: {Preset}, PixFmt: {PixFmt}",
            codecId, width, height, fps, crf, preset, outputPixelFormat);
        _logger.LogDebug("Color metadata - Primaries: {Primaries}, Transfer: {Transfer}, Space: {Space}, Range: {Range}",
            ColorPrimaries, TransferCharacteristic, ColorSpace, ColorRange);
    }

    /// <summary>
    /// Encodes a MagickImage frame and returns the encoded packet.
    /// Pass null to receive remaining packets after flushing.
    /// </summary>
    /// <returns>Encoded packet, or null if more frames are needed.</returns>
    public AVPacket* EncodeFrame(MagickImage? image)
    {
        AVFrame* yuvFrame = null;

        try
        {
            // If image is provided, convert and send it to encoder
            if (image != null)
            {
                // Convert MagickImage to YUV frame (8-bit or 10-bit depending on _use16BitRgb)
                yuvFrame = _pixelConverter.ConvertFromMagickImage(image, _use16BitRgb);

                // Set frame PTS (presentation timestamp)
                yuvFrame->pts = _frameNumber++;

                // Copy color metadata to frame (important for proper encoding)
                if (ColorPrimaries != ColorPrimaries.Unspecified)
                    yuvFrame->color_primaries = (AVColorPrimaries)ColorPrimaries;
                if (TransferCharacteristic != TransferCharacteristic.Unspecified)
                    yuvFrame->color_trc = (AVColorTransferCharacteristic)TransferCharacteristic;
                if (ColorSpace != YuvColorSpace.Unspecified)
                    yuvFrame->colorspace = (AVColorSpace)ColorSpace;
                if (ColorRange != ColorRange.Unspecified)
                    yuvFrame->color_range = (AVColorRange)ColorRange;

                // Send frame to encoder
                var ret = ffmpeg.avcodec_send_frame(_codecContext, yuvFrame);
                if (ret < 0)
                {
                    _logger.LogError("Error sending frame to encoder: {Error}", ret);
                    return null;
                }
            }

            // Receive packet from encoder
            var packet = ffmpeg.av_packet_alloc();
            var receiveRet = ffmpeg.avcodec_receive_packet(_codecContext, packet);

            if (receiveRet == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                // Encoder needs more frames
                ffmpeg.av_packet_free(&packet);
                return null;
            }

            if (receiveRet == ffmpeg.AVERROR_EOF)
            {
                // No more packets
                ffmpeg.av_packet_free(&packet);
                return null;
            }

            if (receiveRet < 0)
            {
                _logger.LogError("Error receiving packet from encoder: {Error}", receiveRet);
                ffmpeg.av_packet_free(&packet);
                return null;
            }

            return packet;
        }
        finally
        {
            if (yuvFrame != null)
            {
                var temp = yuvFrame;
                ffmpeg.av_frame_free(&temp);
            }
        }
    }

    /// <summary>
    /// Flushes the encoder by sending null frame.
    /// Caller should continue calling EncodeFrame with null to get remaining packets.
    /// </summary>
    public void Flush()
    {
        // Send null frame to flush encoder
        ffmpeg.avcodec_send_frame(_codecContext, null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _pixelConverter?.Dispose();

        if (_codecContext != null)
        {
            var codecContext = _codecContext;
            ffmpeg.avcodec_free_context(&codecContext);
        }

        _disposed = true;
    }

    /// <summary>
    /// Tries to parse a pixel format string to AVPixelFormat enum.
    /// Supports common format names like "yuv420p", "yuv420p10le", "yuv422p10le", etc.
    /// </summary>
    private static bool TryParsePixelFormat(string formatName, out AVPixelFormat pixelFormat)
    {
        pixelFormat = formatName?.ToLowerInvariant() switch
        {
            // 8-bit formats
            "yuv420p" => AVPixelFormat.AV_PIX_FMT_YUV420P,
            "yuv422p" => AVPixelFormat.AV_PIX_FMT_YUV422P,
            "yuv444p" => AVPixelFormat.AV_PIX_FMT_YUV444P,

            // 10-bit formats
            "yuv420p10" or "yuv420p10le" => AVPixelFormat.AV_PIX_FMT_YUV420P10LE,
            "yuv420p10be" => AVPixelFormat.AV_PIX_FMT_YUV420P10BE,
            "yuv422p10" or "yuv422p10le" => AVPixelFormat.AV_PIX_FMT_YUV422P10LE,
            "yuv422p10be" => AVPixelFormat.AV_PIX_FMT_YUV422P10BE,
            "yuv444p10" or "yuv444p10le" => AVPixelFormat.AV_PIX_FMT_YUV444P10LE,
            "yuv444p10be" => AVPixelFormat.AV_PIX_FMT_YUV444P10BE,

            // 12-bit formats
            "yuv420p12" or "yuv420p12le" => AVPixelFormat.AV_PIX_FMT_YUV420P12LE,
            "yuv420p12be" => AVPixelFormat.AV_PIX_FMT_YUV420P12BE,
            "yuv422p12" or "yuv422p12le" => AVPixelFormat.AV_PIX_FMT_YUV422P12LE,
            "yuv422p12be" => AVPixelFormat.AV_PIX_FMT_YUV422P12BE,
            "yuv444p12" or "yuv444p12le" => AVPixelFormat.AV_PIX_FMT_YUV444P12LE,
            "yuv444p12be" => AVPixelFormat.AV_PIX_FMT_YUV444P12BE,

            _ => AVPixelFormat.AV_PIX_FMT_NONE
        };

        return pixelFormat != AVPixelFormat.AV_PIX_FMT_NONE;
    }

    /// <summary>
    /// Determines if a pixel format is 10-bit or higher (requires 16-bit RGB conversion).
    /// </summary>
    private static bool Is10BitOrHigher(AVPixelFormat pixelFormat)
    {
        return pixelFormat switch
        {
            AVPixelFormat.AV_PIX_FMT_YUV420P10LE or AVPixelFormat.AV_PIX_FMT_YUV420P10BE or
            AVPixelFormat.AV_PIX_FMT_YUV422P10LE or AVPixelFormat.AV_PIX_FMT_YUV422P10BE or
            AVPixelFormat.AV_PIX_FMT_YUV444P10LE or AVPixelFormat.AV_PIX_FMT_YUV444P10BE or
            AVPixelFormat.AV_PIX_FMT_YUV420P12LE or AVPixelFormat.AV_PIX_FMT_YUV420P12BE or
            AVPixelFormat.AV_PIX_FMT_YUV422P12LE or AVPixelFormat.AV_PIX_FMT_YUV422P12BE or
            AVPixelFormat.AV_PIX_FMT_YUV444P12LE or AVPixelFormat.AV_PIX_FMT_YUV444P12BE => true,
            _ => false
        };
    }
}
