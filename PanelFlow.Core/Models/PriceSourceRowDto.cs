namespace PanelFlow.Core.Models;

/// <summary>某 x_wzdh 的历史价格来源行（BJB + BJFAT）。</summary>
public class PriceSourceRowDto
{
    public string fabh { get; set; } = string.Empty;
    public string? famc { get; set; }
    public string x_bm { get; set; } = string.Empty;
    public string? x_mc { get; set; }
    public string? x_ggxh { get; set; }
    public decimal? x_bj_dj { get; set; }
    public decimal? x_sl { get; set; }
    public DateTime? x_bjb_datetime { get; set; }
    public int dqzt { get; set; }
    public bool IsExcluded { get; set; }
    public bool IsSuspect { get; set; }
    public string? SuspectReason { get; set; }
}
