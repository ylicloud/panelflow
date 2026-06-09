namespace PanelFlow.Core.Models;

/// <summary>STD_PRICE_HISTORY 聚合行（含疑似异常标记）。</summary>
public class PriceHistoryRowDto
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
    public bool IsSuspect { get; set; }
    public string? SuspectReason { get; set; }
    /// <summary>最新价相对均价的偏离百分比，(last_price - avg_price) / avg_price × 100；实时计算，不落库。</summary>
    public decimal? DeviationPercent { get; set; }
}
