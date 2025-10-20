using cascaler.Models;

namespace cascaler.Services.Interfaces;

/// <summary>
/// Orchestrates batch processing of media files (images and videos) with parallel execution.
/// </summary>
public interface IMediaProcessor
{
    /// <summary>
    /// Processes a batch of media files with the specified options.
    /// </summary>
    Task<List<ProcessingResult>> ProcessMediaFilesAsync(
        List<string> inputFiles,
        ProcessingOptions options,
        CancellationToken cancellationToken = default);
}
