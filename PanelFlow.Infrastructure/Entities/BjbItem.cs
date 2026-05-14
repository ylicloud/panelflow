namespace PanelFlow.Infrastructure.Entities;

/// <summary>
/// 报价表明细（BJB）轻量查询实体，仅用于查重。
/// </summary>
public class BjbItem
{
    public string fabh { get; set; } = string.Empty;
    public string x_bm { get; set; } = string.Empty;
    public string x_mc { get; set; } = string.Empty;
    public string x_ggxh { get; set; } = string.Empty;
    public string x_dw { get; set; } = string.Empty;
    public decimal? x_dj { get; set; }
    public decimal? x_sl { get; set; }
    public decimal? x_fdds { get; set; }
    public string x_sccj { get; set; } = string.Empty;
}
