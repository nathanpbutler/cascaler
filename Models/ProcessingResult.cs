namespace nathanbutlerDEV.cascaler.Models;

public class ProcessingResult
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> InfoMessages { get; set; } = new();
}
