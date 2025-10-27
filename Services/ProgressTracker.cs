using Microsoft.Extensions.Options;
using nathanbutlerDEV.cascaler.Infrastructure.Options;
using nathanbutlerDEV.cascaler.Services.Interfaces;
using ShellProgressBar;

namespace nathanbutlerDEV.cascaler.Services;

/// <summary>
/// Consolidates progress tracking and ETA calculation logic to eliminate code duplication.
/// </summary>
public class ProgressTracker : IProgressTracker
{
    private readonly ProcessingSettings _settings;

    public ProgressTracker(IOptions<ProcessingSettings> settings)
    {
        _settings = settings.Value;
    }

    public void UpdateProgress(
        int completedCount,
        int totalCount,
        DateTime startTime,
        ProgressBar? progressBar,
        string itemName,
        bool success = true)
    {
        if (progressBar != null)
        {
            // Calculate and update estimated duration
            var elapsed = DateTime.Now - startTime;

            // Only estimate after we have some data
            if (completedCount >= _settings.MinimumItemsForETA && elapsed.TotalSeconds > 1)
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
        else
        {
            // Output progress to console when progress bar is disabled (don't spam log file)
            var percentage = (completedCount * 100.0 / totalCount);
            var status = success ? "Completed" : "Failed";
            Console.WriteLine($"[{completedCount}/{totalCount}] {percentage:F1}% - {status}: {itemName}");
        }
    }
}
