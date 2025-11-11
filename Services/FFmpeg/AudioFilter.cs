using System.Runtime.InteropServices;
using System.Text;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;

namespace nathanbutlerDEV.cascaler.Services.FFmpeg;

/// <summary>
/// Applies audio filters (vibrato, tremolo) using FFmpeg's avfilter API.
/// This is the key component that enables the vibrato feature.
/// </summary>
public unsafe class AudioFilter : IDisposable
{
    private readonly ILogger<AudioFilter> _logger;
    private readonly AVFilterGraph* _filterGraph;
    private readonly AVFilterContext* _bufferSrcContext;
    private readonly AVFilterContext* _bufferSinkContext;
    private readonly int _sampleRate;
    private readonly int _channels;
    private bool _disposed;

    public AudioFilter(
        int sampleRate,
        int channels,
        string filterDescription,
        ILogger<AudioFilter> logger)
    {
        _logger = logger;
        _sampleRate = sampleRate;
        _channels = channels;

        // Create filter graph
        _filterGraph = ffmpeg.avfilter_graph_alloc();
        if (_filterGraph == null)
            throw new InvalidOperationException("Could not allocate filter graph");

        try
        {
            // Create buffer source (abuffer)
            var abuffer = ffmpeg.avfilter_get_by_name("abuffer");
            if (abuffer == null)
                throw new InvalidOperationException("Could not find abuffer filter");

            AVChannelLayout channelLayout;
            ffmpeg.av_channel_layout_default(&channelLayout, channels);

            // Get channel layout description string for filter initialization
            var layoutDesc = stackalloc byte[64];
            var ret = ffmpeg.av_channel_layout_describe(&channelLayout, layoutDesc, 64);
            if (ret < 0)
                throw new InvalidOperationException($"Could not describe channel layout for {channels} channels");

            var layoutString = Marshal.PtrToStringAnsi((IntPtr)layoutDesc);
            if (string.IsNullOrEmpty(layoutString))
                throw new InvalidOperationException($"Channel layout description is empty for {channels} channels");

            // Build abuffer args with explicit channel_layout to match frames we'll push
            var abufferArgs = $"time_base=1/{sampleRate}:sample_rate={sampleRate}:sample_fmt={(int)AVSampleFormat.AV_SAMPLE_FMT_FLTP}:channel_layout={layoutString}";

            AVFilterContext* bufferSrcCtx;
            if (ffmpeg.avfilter_graph_create_filter(&bufferSrcCtx, abuffer, "in", abufferArgs, null, _filterGraph) < 0)
                throw new InvalidOperationException("Could not create abuffer filter");

            _bufferSrcContext = bufferSrcCtx;

            // Create buffer sink (abuffersink)
            var abuffersink = ffmpeg.avfilter_get_by_name("abuffersink");
            if (abuffersink == null)
                throw new InvalidOperationException("Could not find abuffersink filter");

            AVFilterContext* bufferSinkCtx;
            if (ffmpeg.avfilter_graph_create_filter(&bufferSinkCtx, abuffersink, "out", null, null, _filterGraph) < 0)
                throw new InvalidOperationException("Could not create abuffersink filter");

            _bufferSinkContext = bufferSinkCtx;

            // Force output to float planar format
            var formats = stackalloc AVSampleFormat[2];
            formats[0] = AVSampleFormat.AV_SAMPLE_FMT_FLTP;
            formats[1] = AVSampleFormat.AV_SAMPLE_FMT_NONE; // Terminator
            if (ffmpeg.av_opt_set_bin(_bufferSinkContext, "sample_fmts", (byte*)formats, sizeof(AVSampleFormat) * 2, ffmpeg.AV_OPT_SEARCH_CHILDREN) < 0)
                throw new InvalidOperationException("Could not set output sample format to FLTP");

            // Parse and insert filter chain between source and sink
            var outputs = ffmpeg.avfilter_inout_alloc();
            var inputs = ffmpeg.avfilter_inout_alloc();

            outputs->name = ffmpeg.av_strdup("in");
            outputs->filter_ctx = _bufferSrcContext;
            outputs->pad_idx = 0;
            outputs->next = null;

            inputs->name = ffmpeg.av_strdup("out");
            inputs->filter_ctx = _bufferSinkContext;
            inputs->pad_idx = 0;
            inputs->next = null;

            if (ffmpeg.avfilter_graph_parse_ptr(_filterGraph, filterDescription, &inputs, &outputs, null) < 0)
                throw new InvalidOperationException($"Could not parse filter description: {filterDescription}");

            ffmpeg.avfilter_inout_free(&inputs);
            ffmpeg.avfilter_inout_free(&outputs);

            // Configure the filter graph
            if (ffmpeg.avfilter_graph_config(_filterGraph, null) < 0)
                throw new InvalidOperationException("Could not configure filter graph");

            _logger.LogInformation("Audio filter initialized successfully - SampleRate: {SampleRate}, Channels: {Channels}, Filter: {Filter}",
                sampleRate, channels, filterDescription);
        }
        catch
        {
            // Clean up on error
            if (_filterGraph != null)
            {
                var graph = _filterGraph;
                ffmpeg.avfilter_graph_free(&graph);
            }
            throw;
        }
    }

    /// <summary>
    /// Processes an audio frame through the filter graph.
    /// </summary>
    /// <param name="inputFrame">Input audio frame (float[][] samples)</param>
    /// <returns>List of filtered audio frames (filter may output multiple frames for one input)</returns>
    public List<AudioFrame> ProcessFrame(AudioFrame inputFrame)
    {
        if (inputFrame == null)
            throw new ArgumentNullException(nameof(inputFrame));
        if (inputFrame.SampleData == null)
            throw new ArgumentException("SampleData is null", nameof(inputFrame));

        var result = new List<AudioFrame>();

        // Create AVFrame from AudioFrame
        var avFrame = ffmpeg.av_frame_alloc();
        if (avFrame == null)
            throw new InvalidOperationException("Could not allocate frame for audio filtering");

        avFrame->format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP;
        ffmpeg.av_channel_layout_default(&avFrame->ch_layout, _channels);
        avFrame->sample_rate = _sampleRate;
        avFrame->nb_samples = inputFrame.SamplesPerChannel;

        if (ffmpeg.av_frame_get_buffer(avFrame, 0) < 0)
        {
            var temp = avFrame;
            ffmpeg.av_frame_free(&temp);
            throw new InvalidOperationException("Could not allocate frame buffer for filtering");
        }

        // Copy sample data to AVFrame
        for (var ch = 0; ch < _channels; ch++)
        {
            var dataPtr = (float*)avFrame->data[(uint)ch];
            if (dataPtr == null)
                throw new InvalidOperationException($"Frame data pointer is null for channel {ch}");

            var samples = inputFrame.SampleData[ch];
            if (samples == null)
                throw new ArgumentException($"SampleData[{ch}] is null", nameof(inputFrame));

            for (var i = 0; i < inputFrame.SamplesPerChannel; i++)
            {
                dataPtr[i] = samples[i];
            }
        }

        // Calculate PTS from timestamp
        avFrame->pts = (long)(inputFrame.Timestamp.TotalSeconds * _sampleRate);

        try
        {
            // Push frame to filter graph
            var addResult = ffmpeg.av_buffersrc_add_frame_flags(_bufferSrcContext, avFrame, 0);
            if (addResult < 0)
            {
                var errorBuf = stackalloc byte[256];
                ffmpeg.av_strerror(addResult, errorBuf, 256);
                var errorMsg = Marshal.PtrToStringAnsi((IntPtr)errorBuf);
                throw new InvalidOperationException($"Error adding frame to filter graph: {errorMsg} (code: {addResult})");
            }

            // Pull filtered frames from filter graph
            while (true)
            {
                var filteredFrame = ffmpeg.av_frame_alloc();
                var ret = ffmpeg.av_buffersink_get_frame(_bufferSinkContext, filteredFrame);

                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                {
                    var temp = filteredFrame;
                    ffmpeg.av_frame_free(&temp);
                    break;
                }

                if (ret < 0)
                {
                    _logger.LogWarning("Error getting frame from filter graph: {Error}", ret);
                    var temp = filteredFrame;
                    ffmpeg.av_frame_free(&temp);
                    break;
                }

                // Convert AVFrame to AudioFrame
                var outputSamples = new float[_channels][];
                var samplesPerChannel = filteredFrame->nb_samples;

                for (var ch = 0; ch < _channels; ch++)
                {
                    outputSamples[ch] = new float[samplesPerChannel];
                    var dataPtr = (float*)filteredFrame->data[(uint)ch];
                    if (dataPtr == null)
                        throw new InvalidOperationException($"Filtered frame data pointer is null for channel {ch}");

                    for (var i = 0; i < samplesPerChannel; i++)
                    {
                        outputSamples[ch][i] = dataPtr[i];
                    }
                }

                var timestamp = TimeSpan.FromSeconds((double)filteredFrame->pts / _sampleRate);
                result.Add(new AudioFrame(outputSamples, timestamp));

                var tempFiltered = filteredFrame;
                ffmpeg.av_frame_free(&tempFiltered);
            }
        }
        finally
        {
            var temp = avFrame;
            ffmpeg.av_frame_free(&temp);
        }

        return result;
    }

    /// <summary>
    /// Flushes the filter graph to get any remaining buffered frames.
    /// </summary>
    public List<AudioFrame> Flush()
    {
        var result = new List<AudioFrame>();

        // Send null frame to flush
        ffmpeg.av_buffersrc_add_frame_flags(_bufferSrcContext, null, 0);

        // Pull all remaining frames
        while (true)
        {
            var filteredFrame = ffmpeg.av_frame_alloc();
            var ret = ffmpeg.av_buffersink_get_frame(_bufferSinkContext, filteredFrame);

            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
            {
                var temp = filteredFrame;
                ffmpeg.av_frame_free(&temp);
                break;
            }

            if (ret < 0)
            {
                var temp = filteredFrame;
                ffmpeg.av_frame_free(&temp);
                break;
            }

            // Convert AVFrame to AudioFrame
            var outputSamples = new float[_channels][];
            var samplesPerChannel = filteredFrame->nb_samples;

            for (var ch = 0; ch < _channels; ch++)
            {
                outputSamples[ch] = new float[samplesPerChannel];
                var dataPtr = (float*)filteredFrame->data[(uint)ch];
                for (var i = 0; i < samplesPerChannel; i++)
                {
                    outputSamples[ch][i] = dataPtr[i];
                }
            }

            var timestamp = TimeSpan.FromSeconds((double)filteredFrame->pts / _sampleRate);
            result.Add(new AudioFrame(outputSamples, timestamp));

            var tempFiltered = filteredFrame;
            ffmpeg.av_frame_free(&tempFiltered);
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_filterGraph != null)
        {
            var graph = _filterGraph;
            ffmpeg.avfilter_graph_free(&graph);
        }

        _disposed = true;
    }
}
