namespace PanelFlow.Infrastructure.Entities;

/// <summary>
/// 报价表明细（BJB）实体，映射 BJB 表。
/// </summary>
public class BjbItem
{
    public string fabh { get; set; } = string.Empty;
    public string x_bm { get; set; } = string.Empty;
    public string x_mc { get; set; } = string.Empty;
    public string x_ggxh { get; set; } = string.Empty;
    public string x_dw { get; set; } = string.Empty;
    public decimal? x_dj { get; set; }
    public decimal? x_bj_dj { get; set; }
    public decimal? x_sl { get; set; }
    public decimal? x_fdds { get; set; }
    public string x_sccj { get; set; } = string.Empty;
    public string x_wzdh { get; set; } = string.Empty;

    /// <summary>报价基准单价</summary>
    public decimal? x_bjb_dj { get; set; }

    /// <summary>报价比价</summary>
    public decimal? x_bjb_bj { get; set; }

    /// <summary>类型（11=器件元件行）</summary>
    public int? x_lx { get; set; }

    /// <summary>报价时间</summary>
    public DateTime? x_bjb_datetime { get; set; }
}
