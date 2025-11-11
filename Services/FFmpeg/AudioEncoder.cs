using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace nathanbutlerDEV.cascaler.Services.FFmpeg;

/// <summary>
/// Encodes audio frames to AAC format using FFmpeg.AutoGen.
/// </summary>
public unsafe class AudioEncoder : IDisposable
{
    private readonly ILogger<AudioEncoder> _logger;
    private readonly AVCodecContext* _codecContext;
    private readonly AVCodec* _codec;
    private readonly SwrContext* _swrContext;
    private int _sampleRate; // Not readonly - encoder may adjust this
    private readonly int _channels;
    private readonly int _samplesPerFrame;
    private long _samplesEncoded;
    private bool _disposed;

    public int SampleRate => _sampleRate;
    public int Channels => _channels;
    public int SamplesPerFrame => _samplesPerFrame;
    public AVCodecID CodecID => _codec->id;
    public AVRational TimeBase => _codecContext->time_base;
    public AVCodecContext* CodecContext => _codecContext;

    public AudioEncoder(
        int sampleRate,
        int channels,
        int bitrate,
        ILogger<AudioEncoder> logger)
    {
        _logger = logger;
        _sampleRate = sampleRate;
        _channels = channels;
        _samplesPerFrame = 1024; // AAC standard frame size
        _samplesEncoded = 0;

        // Find AAC encoder
        _codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
        if (_codec == null)
            throw new InvalidOperationException("Could not find AAC encoder");

        // Allocate codec context
        _codecContext = ffmpeg.avcodec_alloc_context3(_codec);
        if (_codecContext == null)
            throw new InvalidOperationException("Could not allocate codec context");

        // Set codec parameters
        _codecContext->sample_rate = sampleRate;
        ffmpeg.av_channel_layout_default(&_codecContext->ch_layout, channels);
        _codecContext->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLTP; // Float planar
        _codecContext->bit_rate = bitrate;
        _codecContext->time_base = new AVRational { num = 1, den = sampleRate };
        _codecContext->profile = ffmpeg.FF_PROFILE_AAC_LOW; // Use AAC-LC profile
        _codecContext->strict_std_compliance = ffmpeg.FF_COMPLIANCE_EXPERIMENTAL; // Allow native AAC encoder

        // Create options dictionary to force AAC-LC profile
        AVDictionary* options = null;
        ffmpeg.av_dict_set(&options, "aac_coder", "twoloop", 0); // Use better AAC encoder

        // Open codec with options
        var openResult = ffmpeg.avcodec_open2(_codecContext, _codec, &options);
        ffmpeg.av_dict_free(&options); // Free options dictionary

        if (openResult < 0)
            throw new InvalidOperationException("Could not open AAC codec");

        // Read actual sample rate from encoder (encoder may have adjusted it)
        // DO NOT overwrite time_base - encoder sets this correctly during avcodec_open2
        _sampleRate = _codecContext->sample_rate;

        _logger.LogInformation("AAC encoder initialized - Profile: {Profile}, SampleRate: {SampleRate} (requested: {RequestedRate}), time_base: {TimeBaseNum}/{TimeBaseDen}, frame_size: {FrameSize}",
            _codecContext->profile, _sampleRate, sampleRate, _codecContext->time_base.num, _codecContext->time_base.den, _codecContext->frame_size);

        if (_sampleRate != sampleRate)
        {
            _logger.LogWarning("AAC encoder changed sample rate from {RequestedRate} to {ActualRate}", sampleRate, _sampleRate);
        }

        // Create resampler (in case input format differs)
        SwrContext* swrContext;
        ffmpeg.swr_alloc_set_opts2(
            &swrContext,
            &_codecContext->ch_layout,
            AVSampleFormat.AV_SAMPLE_FMT_FLTP,
            _sampleRate,  // Use actual sample rate from encoder
            &_codecContext->ch_layout,
            AVSampleFormat.AV_SAMPLE_FMT_FLTP,
            _sampleRate,  // Use actual sample rate from encoder
            0,
            null);

        if (swrContext == null)
            throw new InvalidOperationException("Could not allocate resampler");

        if (ffmpeg.swr_init(swrContext) < 0)
            throw new InvalidOperationException("Could not initialize resampler");

        _swrContext = swrContext;
    }

    /// <summary>
    /// Encodes an audio frame (float[][] samples) and returns the encoded packet.
    /// </summary>
    /// <param name="audioFrame">Audio frame with sample data</param>
    /// <returns>Encoded packet, or null if more frames are needed.</returns>
    public AVPacket* EncodeFrame(AudioFrame audioFrame)
    {
        // Validate frame size
        if (audioFrame.SamplesPerChannel != _samplesPerFrame)
        {
            _logger.LogWarning("Audio frame has {Samples} samples, expected {Expected}. Padding/truncating may occur.",
                audioFrame.SamplesPerChannel, _samplesPerFrame);
        }

        // Create AVFrame and allocate buffer
        var frame = ffmpeg.av_frame_alloc();
        frame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP;
        frame->ch_layout = _codecContext->ch_layout;
        frame->sample_rate = _sampleRate;
        frame->nb_samples = audioFrame.SamplesPerChannel;

        if (ffmpeg.av_frame_get_buffer(frame, 0) < 0)
        {
            var temp = frame;
            ffmpeg.av_frame_free(&temp);
            throw new InvalidOperationException("Could not allocate audio frame buffer");
        }

        // Copy sample data to frame
        for (var ch = 0; ch < _channels; ch++)
        {
            var dataPtr = (float*)frame->data[(uint)ch];
            var samples = audioFrame.SampleData[ch];

            for (var i = 0; i < audioFrame.SamplesPerChannel && i < samples.Length; i++)
            {
                dataPtr[i] = samples[i];
            }
        }

        // Set frame PTS
        frame->pts = _samplesEncoded;
        _samplesEncoded += audioFrame.SamplesPerChannel;

        try
        {
            // Send frame to encoder
            var ret = ffmpeg.avcodec_send_frame(_codecContext, frame);
            if (ret < 0)
            {
                _logger.LogError("Error sending audio frame to encoder: {Error}", ret);
                return null;
            }

            // Receive packet from encoder
            var packet = ffmpeg.av_packet_alloc();
            ret = ffmpeg.avcodec_receive_packet(_codecContext, packet);

            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                // Encoder needs more frames
                ffmpeg.av_packet_free(&packet);
                return null;
            }

            if (ret < 0)
            {
                _logger.LogError("Error receiving audio packet from encoder: {Error}", ret);
                ffmpeg.av_packet_free(&packet);
                return null;
            }

            return packet;
        }
        finally
        {
            var temp = frame;
            ffmpeg.av_frame_free(&temp);
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

    /// <summary>
    /// Receives a packet from the encoder after flushing.
    /// </summary>
    public AVPacket* ReceivePacket()
    {
        var packet = ffmpeg.av_packet_alloc();
        var ret = ffmpeg.avcodec_receive_packet(_codecContext, packet);

        if (ret == ffmpeg.AVERROR_EOF || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret < 0)
        {
            ffmpeg.av_packet_free(&packet);
            return null;
        }

        return packet;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_swrContext != null)
        {
            var swrContext = _swrContext;
            ffmpeg.swr_free(&swrContext);
        }

        if (_codecContext != null)
        {
            var codecContext = _codecContext;
            ffmpeg.avcodec_free_context(&codecContext);
        }

        _disposed = true;
    }
}
