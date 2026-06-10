namespace PanelFlow.Core.Models;

public class PriceHistoryListResult
{
    public IReadOnlyList<PriceHistoryRowDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
