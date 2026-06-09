namespace PanelFlow.Infrastructure.Entities;

/// <summary>
/// STD_PRICE_EXCLUSION 表实体，标记不参与历史价格聚合的报价来源。
/// x_wzdh 为 NULL 表示整单剔除；非空表示仅剔除该单内某型号来源。
/// </summary>
public class StdPriceExclusion
{
    public int Id { get; set; }
    public string fabh { get; set; } = string.Empty;
    public string? x_wzdh { get; set; }
    public string? reason { get; set; }
    public string? created_by { get; set; }
    public DateTime created_at { get; set; }
}
