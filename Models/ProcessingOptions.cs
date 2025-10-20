namespace cascaler.Models;

/// <summary>
/// Encapsulates all processing options from command-line arguments.
/// </summary>
public class ProcessingOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Percent { get; set; }
    public double DeltaX { get; set; } = 1.0;
    public double Rigidity { get; set; } = 1.0;
    public int MaxThreads { get; set; } = 16;
}
