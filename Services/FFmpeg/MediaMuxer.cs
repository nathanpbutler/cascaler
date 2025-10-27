using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace nathanbutlerDEV.cascaler.Services.FFmpeg;

/// <summary>
/// Multiplexes video and audio streams into MP4/MKV containers using FFmpeg.AutoGen.
/// Replaces FFMediaToolkit's MediaBuilder functionality.
/// </summary>
public unsafe class MediaMuxer : IDisposable
{
    private readonly ILogger<MediaMuxer>? _logger;
    private readonly AVFormatContext* _formatContext;
    private readonly AVStream* _videoStream;
    private readonly AVStream* _audioStream;
    private readonly string _outputPath;
    private bool _headerWritten;
    private bool _disposed;

    public bool HasVideo => _videoStream != null;
    public bool HasAudio => _audioStream != null;

    public MediaMuxer(
        string outputPath,
        int videoWidth,
        int videoHeight,
        int videoFps,
        AVCodecID videoCodecId,
        int? audioSampleRate = null,
        int? audioChannels = null,
        ILogger<MediaMuxer>? logger = null)
    {
        _logger = logger;
        _outputPath = outputPath;

        // Determine format from extension
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        var formatName = extension == ".mkv" ? "matroska" : "mp4";

        // Allocate output format context
        AVFormatContext* formatContext = null;
        if (ffmpeg.avformat_alloc_output_context2(&formatContext, null, formatName, outputPath) < 0)
            throw new InvalidOperationException($"Could not allocate output format context for {outputPath}");

        _formatContext = formatContext;

        // Create video stream
        _videoStream = ffmpeg.avformat_new_stream(_formatContext, null);
        if (_videoStream == null)
            throw new InvalidOperationException("Could not create video stream");

        _videoStream->id = 0;
        _videoStream->time_base = new AVRational { num = 1, den = videoFps };
        _videoStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        _videoStream->codecpar->codec_id = videoCodecId;
        _videoStream->codecpar->width = videoWidth;
        _videoStream->codecpar->height = videoHeight;
        _videoStream->codecpar->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;

        // Create audio stream if parameters provided
        if (audioSampleRate.HasValue && audioChannels.HasValue)
        {
            _audioStream = ffmpeg.avformat_new_stream(_formatContext, null);
            if (_audioStream == null)
                throw new InvalidOperationException("Could not create audio stream");

            _audioStream->id = 1;
            _audioStream->time_base = new AVRational { num = 1, den = audioSampleRate.Value };
            _audioStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
            _audioStream->codecpar->codec_id = AVCodecID.AV_CODEC_ID_AAC;
            _audioStream->codecpar->sample_rate = audioSampleRate.Value;
            ffmpeg.av_channel_layout_default(&_audioStream->codecpar->ch_layout, audioChannels.Value);
            _audioStream->codecpar->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP;
        }

        // Open output file
        if ((_formatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
        {
            if (ffmpeg.avio_open(&_formatContext->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE) < 0)
                throw new InvalidOperationException($"Could not open output file: {outputPath}");
        }

        _logger?.LogDebug("Media muxer created - Output: {Output}, Video: {HasVideo}, Audio: {HasAudio}",
            outputPath, HasVideo, HasAudio);
    }

    /// <summary>
    /// Writes the container header. Must be called before writing packets.
    /// </summary>
    public void WriteHeader()
    {
        if (_headerWritten)
            return;

        if (ffmpeg.avformat_write_header(_formatContext, null) < 0)
            throw new InvalidOperationException("Could not write output file header");

        _headerWritten = true;
        _logger?.LogDebug("Media muxer header written");
    }

    /// <summary>
    /// Writes a video packet to the output file.
    /// </summary>
    public void WriteVideoPacket(AVPacket* packet)
    {
        if (!_headerWritten)
            throw new InvalidOperationException("Header must be written before writing packets");

        if (_videoStream == null)
            throw new InvalidOperationException("No video stream available");

        // Set stream index
        packet->stream_index = _videoStream->index;

        // Rescale timestamps to stream time base
        ffmpeg.av_packet_rescale_ts(packet, new AVRational { num = 1, den = _videoStream->time_base.den }, _videoStream->time_base);

        // Write packet
        if (ffmpeg.av_interleaved_write_frame(_formatContext, packet) < 0)
        {
            _logger?.LogWarning("Error writing video packet");
        }
    }

    /// <summary>
    /// Writes an audio packet to the output file.
    /// </summary>
    public void WriteAudioPacket(AVPacket* packet)
    {
        if (!_headerWritten)
            throw new InvalidOperationException("Header must be written before writing packets");

        if (_audioStream == null)
            throw new InvalidOperationException("No audio stream available");

        // Set stream index
        packet->stream_index = _audioStream->index;

        // Rescale timestamps to stream time base
        ffmpeg.av_packet_rescale_ts(packet, new AVRational { num = 1, den = _audioStream->time_base.den }, _audioStream->time_base);

        // Write packet
        if (ffmpeg.av_interleaved_write_frame(_formatContext, packet) < 0)
        {
            _logger?.LogWarning("Error writing audio packet");
        }
    }

    /// <summary>
    /// Writes the container trailer. Must be called after all packets are written.
    /// </summary>
    public void WriteTrailer()
    {
        if (!_headerWritten)
            return;

        if (ffmpeg.av_write_trailer(_formatContext) < 0)
        {
            _logger?.LogWarning("Error writing trailer");
        }

        _logger?.LogInformation("Media muxer completed: {Output}", _outputPath);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Close output file
        if (_formatContext != null && (_formatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
        {
            ffmpeg.avio_closep(&_formatContext->pb);
        }

        // Free format context
        if (_formatContext != null)
        {
            var formatContext = _formatContext;
            ffmpeg.avformat_free_context(formatContext);
        }

        _disposed = true;
    }
}
