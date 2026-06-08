namespace PanelFlow.Core.Models;

public class RenameFabhResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? NewFabh { get; init; }
    public IReadOnlyDictionary<string, int>? AffectedRows { get; init; }
}
