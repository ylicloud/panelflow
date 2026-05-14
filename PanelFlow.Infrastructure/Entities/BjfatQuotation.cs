namespace PanelFlow.Infrastructure.Entities;

/// <summary>
/// 报价方案主表（BJFAT）。
/// </summary>
public class BjfatQuotation
{
    public string fabh { get; set; } = string.Empty;
    public DateTime? fasj { get; set; }
    public string famc { get; set; } = string.Empty;
    public decimal? famxbh { get; set; }
    public string bjr { get; set; } = string.Empty;
    public string bz { get; set; } = string.Empty;
    public string khbh { get; set; } = string.Empty;
    public decimal falx { get; set; }
    public int dqzt { get; set; }
}
