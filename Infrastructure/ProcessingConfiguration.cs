namespace cascaler.Infrastructure;

/// <summary>
/// Application-wide processing configuration. Can be customized through dependency injection.
/// </summary>
public class ProcessingConfiguration
{
    public int MaxImageThreads { get; set; } = Constants.DefaultImageThreads;
    public int MaxVideoThreads { get; set; } = Constants.DefaultVideoThreads;
    public int ProcessingTimeoutSeconds { get; set; } = Constants.ProcessingTimeoutSeconds;
    public int MinimumItemsForETA { get; set; } = Constants.MinimumItemsForETA;
}
