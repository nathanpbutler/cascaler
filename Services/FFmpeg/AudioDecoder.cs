using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace nathanbutlerDEV.cascaler.Services.FFmpeg;

/// <summary>
/// Represents a decoded audio frame with sample data and timestamp.
/// </summary>
public class AudioFrame
{
    /// <summary>Sample data as float[][] (channels × samples)</summary>
    public float[][] SampleData { get; set; }

    /// <summary>Timestamp of this frame</summary>
    public TimeSpan Timestamp { get; set; }

    /// <summary>Number of samples per channel</summary>
    public int SamplesPerChannel => SampleData.Length > 0 ? SampleData[0].Length : 0;

    /// <summary>Number of audio channels</summary>
    public int Channels => SampleData.Length;

    public AudioFrame(float[][] sampleData, TimeSpan timestamp)
    {
        SampleData = sampleData;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Decodes audio frames from a file using FFmpeg.AutoGen.
/// Replaces FFMediaToolkit's audio decoding functionality.
/// </summary>
public unsafe class AudioDecoder : IDisposable
{
    private readonly ILogger<AudioDecoder> _logger;
    private readonly AVFormatContext* _formatContext;
    private readonly AVCodecContext* _codecContext;
    private readonly AVFrame* _frame;
    private readonly AVPacket* _packet;
    private readonly SwrContext* _swrContext;
    private readonly int _streamIndex;
    private bool _disposed;

    public int SampleRate { get; }
    public int Channels { get; }
    public AVSampleFormat SampleFormat { get; }
    public TimeSpan Duration { get; }
    public AVChannelLayout ChannelLayout { get; }

    public AudioDecoder(string filePath, ILogger<AudioDecoder> logger)
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

        // Find best audio stream
        AVCodec* codec = null;
        _streamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &codec, 0);
        if (_streamIndex < 0)
            throw new InvalidOperationException("Could not find audio stream");

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

        // Store audio properties
        SampleRate = _codecContext->sample_rate;
        Channels = _codecContext->ch_layout.nb_channels;
        SampleFormat = _codecContext->sample_fmt;
        ChannelLayout = _codecContext->ch_layout;

        Duration = _formatContext->duration > 0
            ? TimeSpan.FromSeconds((double)_formatContext->duration / ffmpeg.AV_TIME_BASE)
            : TimeSpan.Zero;

        // Create resampler to convert to float planar format (fltp)
        SwrContext* swrContext;
        ffmpeg.swr_alloc_set_opts2(
            &swrContext,
            &_codecContext->ch_layout,
            AVSampleFormat.AV_SAMPLE_FMT_FLTP, // Convert to float planar
            _codecContext->sample_rate,
            &_codecContext->ch_layout,
            _codecContext->sample_fmt,
            _codecContext->sample_rate,
            0,
            null);

        if (swrContext == null)
            throw new InvalidOperationException("Could not allocate resampler");

        if (ffmpeg.swr_init(swrContext) < 0)
            throw new InvalidOperationException("Could not initialize resampler");

        _swrContext = swrContext;

        // Allocate frame and packet
        _frame = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();

        if (_frame == null || _packet == null)
            throw new InvalidOperationException("Could not allocate frame or packet");

        _logger.LogDebug("Audio decoder initialized - SampleRate: {SampleRate}, Channels: {Channels}, Duration: {Duration}",
            SampleRate, Channels, Duration);
    }

    /// <summary>
    /// Decodes the next audio frame from the stream.
    /// </summary>
    /// <returns>True if a frame was decoded, false if end of stream.</returns>
    public bool TryDecodeNextFrame(out AudioFrame audioFrame)
    {
        audioFrame = null!;
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
                        return false;

                    if (error < 0)
                        return false;

                } while (_packet->stream_index != _streamIndex);

                error = ffmpeg.avcodec_send_packet(_codecContext, _packet);
                if (error < 0 && error != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    return false;
            }
            finally
            {
                ffmpeg.av_packet_unref(_packet);
            }

            error = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
        } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));

        if (error < 0)
            return false;

        // Calculate timestamp from original frame before conversion
        var streamTimeBase = _formatContext->streams[_streamIndex]->time_base;
        var pts = _frame->pts != ffmpeg.AV_NOPTS_VALUE ? _frame->pts : 0;
        var timestamp = TimeSpan.FromSeconds(pts * ffmpeg.av_q2d(streamTimeBase));

        // Convert to float planar format
        var convertedFrame = ffmpeg.av_frame_alloc();
        convertedFrame->sample_rate = _frame->sample_rate;
        convertedFrame->ch_layout = _frame->ch_layout;
        convertedFrame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP;
        convertedFrame->nb_samples = _frame->nb_samples;

        if (ffmpeg.av_frame_get_buffer(convertedFrame, 0) < 0)
        {
            var temp = convertedFrame;
            ffmpeg.av_frame_free(&temp);
            return false;
        }

        if (ffmpeg.swr_convert_frame(_swrContext, convertedFrame, _frame) < 0)
        {
            var temp = convertedFrame;
            ffmpeg.av_frame_free(&temp);
            return false;
        }

        // Extract sample data to managed float[][]
        var sampleData = new float[Channels][];
        var samplesPerChannel = convertedFrame->nb_samples;

        for (int ch = 0; ch < Channels; ch++)
        {
            sampleData[ch] = new float[samplesPerChannel];
            var dataPtr = (float*)convertedFrame->data[(uint)ch];
            for (int i = 0; i < samplesPerChannel; i++)
            {
                sampleData[ch][i] = dataPtr[i];
            }
        }

        audioFrame = new AudioFrame(sampleData, timestamp);

        // Free converted frame
        var tempFrame = convertedFrame;
        ffmpeg.av_frame_free(&tempFrame);

        return true;
    }

    /// <summary>
    /// Seeks to a specific timestamp in the audio stream.
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

        // Free resampler
        if (_swrContext != null)
        {
            var swrContext = _swrContext;
            ffmpeg.swr_free(&swrContext);
        }

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
