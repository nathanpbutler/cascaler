using System.ComponentModel.DataAnnotations;

namespace nathanbutlerDEV.cascaler.Infrastructure.Options;

/// <summary>
/// Configuration options for output file naming and progress display.
/// </summary>
public class OutputOptions
{
    /// <summary>
    /// Suffix to append to output file/folder names.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Suffix { get; set; } = "-cas";

    /// <summary>
    /// Character to use for progress bar visualization.
    /// </summary>
    public string ProgressCharacter { get; set; } = "─";

    /// <summary>
    /// Whether to show estimated duration in progress bar.
    /// </summary>
    public bool ShowEstimatedDuration { get; set; } = true;
}
