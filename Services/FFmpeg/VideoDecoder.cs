using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using nathanbutlerDEV.cascaler.Infrastructure;

namespace nathanbutlerDEV.cascaler.Services.FFmpeg;

/// <summary>
/// Decodes video frames from a file using FFmpeg.AutoGen.
/// </summary>
public unsafe class VideoDecoder : IDisposable
{
    private readonly ILogger<VideoDecoder> _logger;
    private readonly AVFormatContext* _formatContext;
    private readonly AVCodecContext* _codecContext;
    private readonly AVFrame* _frame;
    private readonly AVPacket* _packet;
    private readonly int _streamIndex;
    private bool _disposed;

    public string CodecName { get; }
    public int Width { get; }
    public int Height { get; }
    public AVPixelFormat PixelFormat { get; }
    public double FrameRate { get; }
    public int TotalFrames { get; }
    public TimeSpan Duration { get; }

    // Color metadata for HDR/wide gamut support
    public ColorPrimaries ColorPrimaries { get; }
    public TransferCharacteristic TransferCharacteristic { get; }
    public YuvColorSpace ColorSpace { get; }
    public ColorRange ColorRange { get; }
    public int BitDepth { get; }

    public VideoDecoder(string filePath, ILogger<VideoDecoder> logger)
    {
        _logger = logger;

        // Allocate format context
        _formatContext = ffmpeg.avformat_alloc_context();
        if (_formatContext == null)
            throw new InvalidOperationException("Could not allocate format context");

        // Open input file
        AVFormatContext* formatContextPtr = _formatContext;
        if (ffmpeg.avformat_open_input(&formatContextPtr, filePath, null, null) < 0)
            throw new InvalidOperationException($"Could not open input file: {filePath}");

        // Read stream info
        if (ffmpeg.avformat_find_stream_info(_formatContext, null) < 0)
            throw new InvalidOperationException("Could not find stream info");

        // Find best video stream
        AVCodec* codec = null;
        _streamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
        if (_streamIndex < 0)
            throw new InvalidOperationException("Could not find video stream");

        if (codec == null)
            throw new InvalidOperationException("Could not find codec");

        // Allocate codec context
        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (_codecContext == null)
            throw new InvalidOperationException("Could not allocate codec context");

        // Copy codec parameters to context
        var stream = _formatContext->streams[_streamIndex];
        if (ffmpeg.avcodec_parameters_to_context(_codecContext, stream->codecpar) < 0)
            throw new InvalidOperationException("Could not copy codec parameters");

        // Open codec
        if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
            throw new InvalidOperationException("Could not open codec");

        // Store video properties
        CodecName = ffmpeg.avcodec_get_name(codec->id) ?? "unknown";
        Width = _codecContext->width;
        Height = _codecContext->height;
        PixelFormat = _codecContext->pix_fmt;

        // Calculate frame rate
        if (stream->avg_frame_rate.num > 0 && stream->avg_frame_rate.den > 0)
        {
            FrameRate = (double)stream->avg_frame_rate.num / stream->avg_frame_rate.den;
        }
        else
        {
            FrameRate = 25.0; // Default fallback
        }

        // Calculate total frames and duration
        if (stream->nb_frames > 0)
        {
            TotalFrames = (int)stream->nb_frames;
        }
        else if (_formatContext->duration > 0)
        {
            var durationSeconds = (double)_formatContext->duration / ffmpeg.AV_TIME_BASE;
            TotalFrames = (int)(durationSeconds * FrameRate);
        }
        else
        {
            TotalFrames = 0; // Unknown
        }

        Duration = _formatContext->duration > 0
            ? TimeSpan.FromSeconds((double)_formatContext->duration / ffmpeg.AV_TIME_BASE)
            : TimeSpan.Zero;

        // Capture color metadata from codec context
        ColorPrimaries = (ColorPrimaries)_codecContext->color_primaries;
        TransferCharacteristic = (TransferCharacteristic)_codecContext->color_trc;
        ColorSpace = (YuvColorSpace)_codecContext->colorspace;
        ColorRange = (ColorRange)_codecContext->color_range;

        // Detect bit depth from pixel format
        BitDepth = GetBitDepthFromPixelFormat(_codecContext->pix_fmt);

        // Allocate frame and packet
        _frame = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();

        if (_frame == null || _packet == null)
            throw new InvalidOperationException("Could not allocate frame or packet");

        _logger.LogDebug("Video decoder initialized - Codec: {Codec}, Size: {Width}x{Height}, FPS: {FPS}, Frames: {Frames}, BitDepth: {BitDepth}",
            CodecName, Width, Height, FrameRate, TotalFrames, BitDepth);
        _logger.LogDebug("Color metadata - Primaries: {Primaries}, Transfer: {Transfer}, Space: {Space}, Range: {Range}",
            ColorPrimaries, TransferCharacteristic, ColorSpace, ColorRange);
    }

    /// <summary>
    /// Determines bit depth from pixel format.
    /// </summary>
    private static int GetBitDepthFromPixelFormat(AVPixelFormat pixelFormat)
    {
        return pixelFormat switch
        {
            // 10-bit formats
            AVPixelFormat.AV_PIX_FMT_YUV420P10LE or AVPixelFormat.AV_PIX_FMT_YUV420P10BE or
            AVPixelFormat.AV_PIX_FMT_YUV422P10LE or AVPixelFormat.AV_PIX_FMT_YUV422P10BE or
            AVPixelFormat.AV_PIX_FMT_YUV444P10LE or AVPixelFormat.AV_PIX_FMT_YUV444P10BE or
            AVPixelFormat.AV_PIX_FMT_GBRP10LE or AVPixelFormat.AV_PIX_FMT_GBRP10BE => 10,

            // 12-bit formats
            AVPixelFormat.AV_PIX_FMT_YUV420P12LE or AVPixelFormat.AV_PIX_FMT_YUV420P12BE or
            AVPixelFormat.AV_PIX_FMT_YUV422P12LE or AVPixelFormat.AV_PIX_FMT_YUV422P12BE or
            AVPixelFormat.AV_PIX_FMT_YUV444P12LE or AVPixelFormat.AV_PIX_FMT_YUV444P12BE or
            AVPixelFormat.AV_PIX_FMT_GBRP12LE or AVPixelFormat.AV_PIX_FMT_GBRP12BE => 12,

            // 16-bit formats
            AVPixelFormat.AV_PIX_FMT_YUV420P16LE or AVPixelFormat.AV_PIX_FMT_YUV420P16BE or
            AVPixelFormat.AV_PIX_FMT_YUV422P16LE or AVPixelFormat.AV_PIX_FMT_YUV422P16BE or
            AVPixelFormat.AV_PIX_FMT_YUV444P16LE or AVPixelFormat.AV_PIX_FMT_YUV444P16BE or
            AVPixelFormat.AV_PIX_FMT_RGB48LE or AVPixelFormat.AV_PIX_FMT_RGB48BE or
            AVPixelFormat.AV_PIX_FMT_GBRP16LE or AVPixelFormat.AV_PIX_FMT_GBRP16BE => 16,

            // Default to 8-bit for all other formats
            _ => 8
        };
    }

    /// <summary>
    /// Decodes the next frame from the video stream.
    /// </summary>
    /// <returns>True if a frame was decoded, false if end of stream.</returns>
    public bool TryDecodeNextFrame(out AVFrame frame)
    {
        ffmpeg.av_frame_unref(_frame);
        int error;

        do
        {
            try
            {
                do
                {
                    ffmpeg.av_packet_unref(_packet);
                    error = ffmpeg.av_read_frame(_formatContext, _packet);

                    if (error == ffmpeg.AVERROR_EOF)
                    {
                        frame = *_frame;
                        return false;
                    }

                    if (error < 0)
                    {
                        frame = *_frame;
                        return false;
                    }
                } while (_packet->stream_index != _streamIndex);

                error = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                if (error < 0 && error != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    frame = *_frame;
                    return false;
                }
            }
            finally
            {
                ffmpeg.av_packet_unref(_packet);
            }

            error = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
        } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));

        if (error < 0)
        {
            frame = *_frame;
            return false;
        }

        frame = *_frame;
        return true;
    }

    /// <summary>
    /// Seeks to a specific timestamp in the video.
    /// </summary>
    public void SeekTo(TimeSpan timestamp)
    {
        var timestampValue = (long)(timestamp.TotalSeconds * ffmpeg.AV_TIME_BASE);
        var streamTimeBase = _formatContext->streams[_streamIndex]->time_base;
        var streamTimestamp = ffmpeg.av_rescale_q(timestampValue, new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE }, streamTimeBase);

        if (ffmpeg.av_seek_frame(_formatContext, _streamIndex, streamTimestamp, ffmpeg.AVSEEK_FLAG_BACKWARD) < 0)
        {
            _logger.LogWarning("Failed to seek to timestamp {Timestamp}", timestamp);
        }

        // Flush codec buffers
        ffmpeg.avcodec_flush_buffers(_codecContext);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Free frame
        if (_frame != null)
        {
            var frame = _frame;
            ffmpeg.av_frame_free(&frame);
        }

        // Free packet
        if (_packet != null)
        {
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);
        }

        // Close codec
        if (_codecContext != null)
        {
            var codecContext = _codecContext;
            ffmpeg.avcodec_free_context(&codecContext);
        }

        // Close format context
        if (_formatContext != null)
        {
            var formatContext = _formatContext;
            ffmpeg.avformat_close_input(&formatContext);
        }

        _disposed = true;
    }
}
