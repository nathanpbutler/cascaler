using FFmpeg.AutoGen;
using ImageMagick;
using Microsoft.Extensions.Logging;
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
    private long _frameNumber;
    private bool _disposed;

    public int Width => _width;
    public int Height => _height;
    public int Fps => _fps;
    public AVCodecID CodecID => _codec->id;
    public AVRational TimeBase => _codecContext->time_base;

    public VideoEncoder(
        int width,
        int height,
        int fps,
        AVCodecID codecId,
        int crf,
        string preset,
        ILogger<VideoEncoder> logger)
    {
        _logger = logger;
        _width = width;
        _height = height;
        _fps = fps;
        _frameNumber = 0;

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
        _codecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
        _codecContext->gop_size = fps * 2; // Keyframe every 2 seconds
        _codecContext->max_b_frames = 2;

        // Set CRF (Constant Rate Factor) for quality
        ffmpeg.av_opt_set_int(_codecContext->priv_data, "crf", crf, 0);

        // Set encoding preset
        ffmpeg.av_opt_set(_codecContext->priv_data, "preset", preset, 0);

        // Open codec
        if (ffmpeg.avcodec_open2(_codecContext, _codec, null) < 0)
            throw new InvalidOperationException("Could not open codec");

        // Create pixel format converter (RGB24 -> YUV420P)
        _pixelConverter = new PixelFormatConverter(
            width,
            height,
            AVPixelFormat.AV_PIX_FMT_RGB24,
            AVPixelFormat.AV_PIX_FMT_YUV420P);

        _logger.LogDebug("Video encoder initialized - Codec: {Codec}, Size: {Width}x{Height}, FPS: {FPS}, CRF: {CRF}, Preset: {Preset}",
            codecId, width, height, fps, crf, preset);
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
                // Convert MagickImage to YUV420P frame
                yuvFrame = _pixelConverter.ConvertFromMagickImage(image);

                // Set frame PTS (presentation timestamp)
                yuvFrame->pts = _frameNumber++;

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
}
