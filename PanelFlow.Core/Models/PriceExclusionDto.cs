namespace PanelFlow.Core.Models;

/// <summary>STD_PRICE_EXCLUSION 剔除记录。</summary>
public class PriceExclusionDto
{
    public int Id { get; set; }
    public string fabh { get; set; } = string.Empty;
    public string? x_wzdh { get; set; }
    public string? reason { get; set; }
    public string? created_by { get; set; }
    public DateTime created_at { get; set; }
    public bool IsWholeQuotation { get; set; }
}
