using ShellProgressBar;

namespace nathanbutlerDEV.cascaler.Infrastructure;

/// <summary>
/// Thread-safe singleton that holds reference to the currently active progress bar.
/// Allows logging system to coordinate output with progress bar updates.
/// </summary>
public class ProgressBarContext : IProgressBarContext
{
    private ProgressBar? _currentProgressBar;
    private readonly object _lock = new();

    /// <inheritdoc/>
    public ProgressBar? Current
    {
        get
        {
            lock (_lock)
            {
                return _currentProgressBar;
            }
        }
    }

    /// <inheritdoc/>
    public void SetProgressBar(ProgressBar? progressBar)
    {
        lock (_lock)
        {
            _currentProgressBar = progressBar;
        }
    }

    /// <inheritdoc/>
    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _currentProgressBar != null;
            }
        }
    }
}
