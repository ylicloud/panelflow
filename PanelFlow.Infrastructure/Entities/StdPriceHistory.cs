namespace PanelFlow.Infrastructure.Entities;

/// <summary>
/// STD_PRICE_HISTORY 表实体，存储按 x_wzdh（标准化指纹）聚合的历史报价数据。
/// 由 SP_RefreshPriceHistory 存储过程每日刷新。
/// </summary>
public class StdPriceHistory
{
    public int Id { get; set; }
    public string x_wzdh { get; set; } = string.Empty;
    public string? ggxh { get; set; }
    public string? x_mc { get; set; }
    public string? x_dw { get; set; }
    public string? x_sccj { get; set; }
    public decimal last_price { get; set; }
    public string? last_fabh { get; set; }
    public DateTime? last_date { get; set; }
    public decimal? avg_price { get; set; }
    public int avg_count { get; set; }
    public decimal? min_price { get; set; }
    public decimal? max_price { get; set; }
    public DateTime? updated_at { get; set; }
}
