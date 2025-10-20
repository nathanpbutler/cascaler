using cascaler.Infrastructure;
using cascaler.Services.Interfaces;
using ShellProgressBar;

namespace cascaler.Services;

/// <summary>
/// Consolidates progress tracking and ETA calculation logic to eliminate code duplication.
/// </summary>
public class ProgressTracker : IProgressTracker
{
    private readonly ProcessingConfiguration _config;

    public ProgressTracker(ProcessingConfiguration config)
    {
        _config = config;
    }

    public void UpdateProgress(
        int completedCount,
        int totalCount,
        DateTime startTime,
        ProgressBar progressBar,
        string itemName,
        bool success = true)
    {
        // Calculate and update estimated duration
        var elapsed = DateTime.Now - startTime;

        // Only estimate after we have some data
        if (completedCount >= _config.MinimumItemsForETA && elapsed.TotalSeconds > 1)
        {
            var throughput = completedCount / elapsed.TotalSeconds; // items per second
            var remaining = totalCount - completedCount;
            if (remaining > 0 && throughput > 0)
            {
                var estimatedTimeRemaining = TimeSpan.FromSeconds(remaining / throughput);
                progressBar.EstimatedDuration = estimatedTimeRemaining;
            }
        }

        // Update progress bar with item name and status
        progressBar.Tick($"{(success ? "Completed" : "Failed")}: {itemName}");
    }
}
