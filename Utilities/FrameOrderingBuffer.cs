using ImageMagick;

namespace cascaler.Utilities;

/// <summary>
/// Thread-safe buffer that accepts frames in any order and releases them sequentially.
/// Used to maintain frame ordering when parallel processing completes out-of-order.
/// </summary>
public class FrameOrderingBuffer : IDisposable
{
    private readonly int _totalFrames;
    private readonly Dictionary<int, MagickImage> _buffer;
    private readonly SemaphoreSlim _semaphore;
    private int _nextFrameToRelease;
    private bool _isComplete;
    private readonly TaskCompletionSource<bool> _completionSource;

    public FrameOrderingBuffer(int totalFrames)
    {
        _totalFrames = totalFrames;
        _buffer = new Dictionary<int, MagickImage>();
        _semaphore = new SemaphoreSlim(1, 1);
        _nextFrameToRelease = 0;
        _isComplete = false;
        _completionSource = new TaskCompletionSource<bool>();
    }

    /// <summary>
    /// Adds a completed frame to the buffer.
    /// </summary>
    /// <param name="frameIndex">Zero-based frame index.</param>
    /// <param name="frame">The processed frame image.</param>
    public async Task AddFrameAsync(int frameIndex, MagickImage frame)
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_isComplete)
            {
                frame.Dispose();
                return;
            }

            // Clone the frame to avoid disposal issues
            _buffer[frameIndex] = (MagickImage)frame.Clone();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Attempts to get the next sequential frame that's ready.
    /// </summary>
    /// <returns>The next frame if available, or null if waiting for earlier frames.</returns>
    public async Task<(int frameIndex, MagickImage? frame)?> TryGetNextFrameAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            // Check if we're done
            if (_nextFrameToRelease >= _totalFrames)
            {
                if (!_isComplete)
                {
                    _isComplete = true;
                    _completionSource.TrySetResult(true);
                }
                return null;
            }

            // Check if the next frame is ready
            if (_buffer.TryGetValue(_nextFrameToRelease, out var frame))
            {
                _buffer.Remove(_nextFrameToRelease);
                var currentIndex = _nextFrameToRelease;
                _nextFrameToRelease++;

                return (currentIndex, frame);
            }

            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Waits until all frames have been added and released.
    /// </summary>
    public Task WaitForCompletionAsync() => _completionSource.Task;

    /// <summary>
    /// Signals that no more frames will be added (useful for early termination).
    /// </summary>
    public async Task CompleteAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!_isComplete)
            {
                _isComplete = true;
                _completionSource.TrySetResult(true);

                // Dispose any remaining buffered frames
                foreach (var frame in _buffer.Values)
                {
                    frame.Dispose();
                }
                _buffer.Clear();
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Gets the current buffer size (number of frames waiting to be released).
    /// </summary>
    public int BufferSize
    {
        get
        {
            _semaphore.Wait();
            try
            {
                return _buffer.Count;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    public void Dispose()
    {
        _semaphore.Wait();
        try
        {
            foreach (var frame in _buffer.Values)
            {
                frame.Dispose();
            }
            _buffer.Clear();
        }
        finally
        {
            _semaphore.Release();
            _semaphore.Dispose();
        }
    }
}
