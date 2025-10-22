using ShellProgressBar;

namespace cascaler.Services.Interfaces;

/// <summary>
/// Handles progress tracking and ETA calculations for batch processing operations.
/// </summary>
public interface IProgressTracker
{
    /// <summary>
    /// Updates progress bar with current completion status and calculates ETA.
    /// </summary>
    /// <param name="completedCount">Number of items completed</param>
    /// <param name="totalCount">Total number of items to process</param>
    /// <param name="startTime">Time when processing started</param>
    /// <param name="progressBar">Progress bar to update (null if progress display is disabled)</param>
    /// <param name="itemName">Name of the current item being processed</param>
    /// <param name="success">Whether the item was processed successfully</param>
    void UpdateProgress(
        int completedCount,
        int totalCount,
        DateTime startTime,
        ProgressBar? progressBar,
        string itemName,
        bool success = true);
}
