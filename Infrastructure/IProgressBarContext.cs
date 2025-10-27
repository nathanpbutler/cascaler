using ShellProgressBar;

namespace nathanbutlerDEV.cascaler.Infrastructure;

/// <summary>
/// Provides context for the currently active progress bar, allowing logging
/// to be coordinated with progress bar updates.
/// </summary>
public interface IProgressBarContext
{
    /// <summary>
    /// Gets the currently active progress bar, if any.
    /// </summary>
    ProgressBar? Current { get; }

    /// <summary>
    /// Sets the currently active progress bar.
    /// </summary>
    /// <param name="progressBar">The progress bar to set as active, or null to clear.</param>
    void SetProgressBar(ProgressBar? progressBar);

    /// <summary>
    /// Checks if a progress bar is currently active.
    /// </summary>
    bool IsActive { get; }
}
