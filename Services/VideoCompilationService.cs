using FFmpeg.AutoGen;
using ImageMagick;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using nathanbutlerDEV.cascaler.Infrastructure;
using nathanbutlerDEV.cascaler.Infrastructure.Options;
using nathanbutlerDEV.cascaler.Models;
using nathanbutlerDEV.cascaler.Services.FFmpeg;
using nathanbutlerDEV.cascaler.Services.Interfaces;
using nathanbutlerDEV.cascaler.Utilities;

namespace nathanbutlerDEV.cascaler.Services;

/// <summary>
/// Handles video compilation using FFmpeg.AutoGen.
/// Supports unified video+audio encoding with optional vibrato/tremolo filtering.
/// </summary>
public class VideoCompilationService : IVideoCompilationService
{
    private readonly FFmpegConfiguration _ffmpegConfig;
    private readonly VideoEncodingOptions _encodingOptions;
    private readonly ILogger<VideoCompilationService> _logger;

    public VideoCompilationService(
        FFmpegConfiguration ffmpegConfig,
        IOptions<VideoEncodingOptions> encodingOptions,
        ILogger<VideoCompilationService> logger)
    {
        _ffmpegConfig = ffmpegConfig;
        _encodingOptions = encodingOptions.Value;
        _logger = logger;
    }

    public async Task<(Func<int, MagickImage, Task> submitFrame, Task encodingComplete)> StartStreamingEncoderWithAudioAsync(
        string? sourceVideoPath,
        string outputVideoPath,
        int width,
        int height,
        double fps,
        int totalFrames,
        ProcessingOptions options,
        double? startTime = null,
        double? duration = null,
        CancellationToken cancellationToken = default)
    {
        _ffmpegConfig.Initialize();

        // Create frame ordering buffer
        var frameBuffer = new FrameOrderingBuffer(totalFrames);

        // Extract and optionally filter audio
        List<AudioFrame>? audioFrames = null;
        int? sampleRate = null;
        int? channels = null;

        if (!string.IsNullOrEmpty(sourceVideoPath))
        {
            audioFrames = await ExtractAndFilterAudioAsync(sourceVideoPath, options.Vibrato, startTime, duration, cancellationToken);

            if (audioFrames != null && audioFrames.Count > 0)
            {
                using var tempDecoder = new AudioDecoder(sourceVideoPath, NullLogger<AudioDecoder>.Instance);
                sampleRate = tempDecoder.SampleRate;
                channels = tempDecoder.Channels;

                _logger.LogInformation("Extracted {AudioFrameCount} audio frames (SampleRate: {SampleRate}, Channels: {Channels})",
                    audioFrames.Count, sampleRate, channels);
            }
        }

        // Start encoding task
        var encodingTask = EncodeVideoWithAudioAsync(
            outputVideoPath,
            frameBuffer,
            width,
            height,
            fps,
            totalFrames,
            audioFrames,
            sampleRate,
            channels,
            options,
            cancellationToken);

        // Return submit function and completion task
        Func<int, MagickImage, Task> submitFrame = async (frameIndex, frame) =>
        {
            await frameBuffer.AddFrameAsync(frameIndex, frame);
        };

        return (submitFrame, encodingTask);
    }

    private async Task<List<AudioFrame>?> ExtractAndFilterAudioAsync(
        string videoPath,
        bool applyVibrato,
        double? startTime,
        double? duration,
        CancellationToken cancellationToken)
    {
        try
        {
            using var audioDecoder = new AudioDecoder(videoPath, NullLogger<AudioDecoder>.Instance);

            var audioFrames = new List<AudioFrame>();
            var startTimeSpan = startTime.HasValue ? TimeSpan.FromSeconds(startTime.Value) : TimeSpan.Zero;
            var endTimeSpan = duration.HasValue
                ? TimeSpan.FromSeconds(startTime.GetValueOrDefault() + duration.Value)
                : TimeSpan.MaxValue;

            _logger.LogDebug("Audio extraction - Start: {StartTime}s, End: {EndTime}s", startTimeSpan.TotalSeconds, endTimeSpan.TotalSeconds);

            // Seek if needed
            if (startTime.HasValue)
            {
                audioDecoder.SeekTo(startTimeSpan);
                _logger.LogDebug("Seeked to {StartTime}s", startTimeSpan.TotalSeconds);
            }

            int totalFramesRead = 0;
            int framesInRange = 0;

            // Decode audio frames
            while (audioDecoder.TryDecodeNextFrame(out var audioFrame))
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalFramesRead++;

                if (totalFramesRead <= 5 || audioFrames.Count <= 5)
                {
                    _logger.LogDebug("Audio frame {Index}: Timestamp={Timestamp}s, Samples={Samples}",
                        totalFramesRead, audioFrame.Timestamp.TotalSeconds, audioFrame.SamplesPerChannel);
                }

                if (audioFrame.Timestamp < startTimeSpan)
                    continue;

                if (audioFrame.Timestamp > endTimeSpan)
                {
                    _logger.LogDebug("Reached end time at frame {Index}, stopping", totalFramesRead);
                    break;
                }

                // Adjust timestamp relative to start
                var adjustedTimestamp = audioFrame.Timestamp - startTimeSpan;
                audioFrame.Timestamp = adjustedTimestamp;

                audioFrames.Add(audioFrame);
                framesInRange++;
            }

            _logger.LogInformation("Audio extraction complete - Total frames read: {TotalRead}, Frames in range: {InRange}",
                totalFramesRead, framesInRange);

            // Apply vibrato filter if requested
            if (applyVibrato && audioFrames.Count > 0)
            {
                _logger.LogInformation("Applying vibrato and tremolo audio effects");
                audioFrames = ApplyAudioFilter(audioFrames, audioDecoder.SampleRate, audioDecoder.Channels);
            }

            return audioFrames;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting audio from {VideoPath}", videoPath);
            return null;
        }
    }

    private List<AudioFrame> ApplyAudioFilter(List<AudioFrame> audioFrames, int sampleRate, int channels)
    {
        var filteredFrames = new List<AudioFrame>();

        using var audioFilter = new AudioFilter(
            sampleRate,
            channels,
            "vibrato=d=1,tremolo", // Vibrato with duration=1, followed by tremolo
            _logger as ILogger<AudioFilter> ?? NullLogger<AudioFilter>.Instance);

        foreach (var frame in audioFrames)
        {
            var outputFrames = audioFilter.ProcessFrame(frame);
            filteredFrames.AddRange(outputFrames);
        }

        // Flush filter to get remaining frames
        var remaining = audioFilter.Flush();
        filteredFrames.AddRange(remaining);

        _logger.LogInformation("Audio filtering complete - Input: {InputFrames}, Output: {OutputFrames}",
            audioFrames.Count, filteredFrames.Count);

        return filteredFrames;
    }

    private async Task EncodeVideoWithAudioAsync(
        string outputVideoPath,
        FrameOrderingBuffer frameBuffer,
        int width,
        int height,
        double fps,
        int totalFrames,
        List<AudioFrame>? audioFrames,
        int? audioSampleRate,
        int? audioChannels,
        ProcessingOptions options,
        CancellationToken cancellationToken)
    {
        VideoEncoder? videoEncoder = null;
        AudioEncoder? audioEncoder = null;
        MediaMuxer? muxer = null;

        try
        {
            // Map codec and preset
            var codecId = MapCodecToCodecId(options.Codec ?? _encodingOptions.DefaultCodec);
            var preset = options.Preset ?? _encodingOptions.DefaultPreset;
            var crf = options.CRF ?? _encodingOptions.DefaultCRF;

            // Create video encoder
            videoEncoder = new VideoEncoder(width, height, (int)Math.Round(fps), codecId, crf, preset, NullLogger<VideoEncoder>.Instance);

            // Create audio encoder if audio present
            if (audioFrames != null && audioSampleRate.HasValue && audioChannels.HasValue)
            {
                // Split audio frames to AAC frame size (1024 samples)
                audioFrames = SplitToAACFrameSize(audioFrames, audioSampleRate.Value);

                audioEncoder = new AudioEncoder(audioSampleRate.Value, audioChannels.Value, 256000, NullLogger<AudioEncoder>.Instance);
            }

            // Create muxer with encoder's actual sample rate (encoder may have adjusted it)
            muxer = new MediaMuxer(
                outputVideoPath,
                width,
                height,
                (int)Math.Round(fps),
                codecId,
                audioEncoder?.SampleRate,
                audioEncoder?.Channels,
                NullLogger<MediaMuxer>.Instance);

            // Set encoder time_bases for proper timestamp rescaling
            muxer.SetVideoEncoderTimeBase(videoEncoder.TimeBase);
            if (audioEncoder != null)
            {
                muxer.SetAudioEncoderTimeBase(audioEncoder.TimeBase);

                // Copy encoder codec parameters to stream (profile, extradata, etc.)
                unsafe
                {
                    muxer.SetAudioEncoderParameters(audioEncoder.CodecContext);
                }
            }

            muxer.WriteHeader();

            int framesWritten = 0;
            int audioFrameIndex = 0;

            _logger.LogInformation("Starting unified video encoder for {TotalFrames} frames at {FPS} fps", totalFrames, fps);
            if (audioFrames != null)
            {
                _logger.LogDebug("Audio frames available: {AudioFrameCount}", audioFrames.Count);
            }
            else
            {
                _logger.LogDebug("No audio frames available");
            }

            // Process frames
            while (framesWritten < totalFrames && !cancellationToken.IsCancellationRequested)
            {
                var result = await frameBuffer.TryGetNextFrameAsync();

                if (result == null)
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                var (frameIndex, frame) = result.Value;
                if (frame == null)
                    break;

                using (frame)
                {
#pragma warning disable CS9123 // The '&' operator should not be used on parameters or local variables in async methods
                    unsafe
                    {
                        // Encode video frame
                        var vidPacket = videoEncoder.EncodeFrame(frame);
                        if (vidPacket != null)
                        {
                            muxer.WriteVideoPacket(vidPacket);
                            var packetToFree = vidPacket;
                            ffmpeg.av_packet_free(&packetToFree);
                        }

                        framesWritten++;

                        // Encode corresponding audio frames
                        if (audioFrames != null && audioEncoder != null)
                        {
                            var currentVideoTime = TimeSpan.FromSeconds(framesWritten / fps);

                            while (audioFrameIndex < audioFrames.Count)
                            {
                                var audioFrame = audioFrames[audioFrameIndex];

                                if (audioFrame.Timestamp <= currentVideoTime)
                                {
                                    var audPacket = audioEncoder.EncodeFrame(audioFrame);
                                    if (audPacket != null)
                                    {
                                        muxer.WriteAudioPacket(audPacket);
                                        var audioPacketToFree = audPacket;
                                        ffmpeg.av_packet_free(&audioPacketToFree);
                                    }

                                    audioFrameIndex++;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }
#pragma warning restore CS9123
                }
            }

            // Flush encoders
            videoEncoder.Flush();
            if (audioEncoder != null)
                audioEncoder.Flush();

#pragma warning disable CS9123 // The '&' operator should not be used on parameters or local variables in async methods
            unsafe
            {
                // Get remaining video packets
                AVPacket* videoPacket;
                while ((videoPacket = videoEncoder.EncodeFrame(null!)) != null)
                {
                    muxer.WriteVideoPacket(videoPacket);
                    var packetToFree = videoPacket;
                    ffmpeg.av_packet_free(&packetToFree);
                }

                // Get remaining audio packets
                if (audioEncoder != null)
                {
                    AVPacket* audioPacket;
                    while ((audioPacket = audioEncoder.ReceivePacket()) != null)
                    {
                        muxer.WriteAudioPacket(audioPacket);
                        var audioPacketToFree = audioPacket;
                        ffmpeg.av_packet_free(&audioPacketToFree);
                    }
                }

                muxer.WriteTrailer();
            }
#pragma warning restore CS9123

            _logger.LogInformation("Video encoding completed: {FramesWritten} frames written", framesWritten);
            if (audioFrames != null)
            {
                _logger.LogInformation("Audio encoding completed: {AudioFrames} frames written", audioFrameIndex);
            }
        }
        finally
        {
            await frameBuffer.CompleteAsync();
            videoEncoder?.Dispose();
            audioEncoder?.Dispose();
            muxer?.Dispose();
        }
    }

    private List<AudioFrame> SplitToAACFrameSize(List<AudioFrame> sourceFrames, int sampleRate)
    {
        const int aacFrameSize = 1024;
        var result = new List<AudioFrame>();

        if (sourceFrames.Count == 0)
            return result;

        var startTimestamp = sourceFrames[0].Timestamp;
        int totalSamplesProcessed = 0;

        foreach (var sourceFrame in sourceFrames)
        {
            var samplesPerChannel = sourceFrame.SamplesPerChannel;
            var channels = sourceFrame.Channels;

            for (int offset = 0; offset < samplesPerChannel; offset += aacFrameSize)
            {
                var remainingSamples = Math.Min(aacFrameSize, samplesPerChannel - offset);
                var chunkData = new float[channels][];

                for (int ch = 0; ch < channels; ch++)
                {
                    chunkData[ch] = new float[remainingSamples];
                    Array.Copy(sourceFrame.SampleData[ch], offset, chunkData[ch], 0, remainingSamples);
                }

                var chunkTimestamp = startTimestamp + TimeSpan.FromSeconds((double)totalSamplesProcessed / sampleRate);
                totalSamplesProcessed += remainingSamples;

                result.Add(new AudioFrame(chunkData, chunkTimestamp));
            }
        }

        return result;
    }

    private static AVCodecID MapCodecToCodecId(string codec)
    {
        return codec?.ToLowerInvariant() switch
        {
            "libx265" or "h265" or "hevc" => AVCodecID.AV_CODEC_ID_HEVC,
            "libx264" or "h264" or "avc" => AVCodecID.AV_CODEC_ID_H264,
            _ => AVCodecID.AV_CODEC_ID_H264
        };
    }

    public Task<string> DetermineOutputContainerAsync(string? audioPath)
    {
        return Task.FromResult(".mp4");
    }
}
