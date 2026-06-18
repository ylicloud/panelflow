namespace PanelFlow.Infrastructure.Entities;

/// <summary>项目元件汇总表（BJB_XMYJHZ），PB 汇总输出，只读。</summary>
public class BjbXmyjhzItem
{
    public string fabh { get; set; } = string.Empty;
    public string x_flbh { get; set; } = string.Empty;
    public string x_ggxh { get; set; } = string.Empty;
    public string x_sccj { get; set; } = string.Empty;
    public string x_key_ry { get; set; } = string.Empty;
    public string? x_mc { get; set; }
    public string x_dw { get; set; } = string.Empty;
    public decimal x_sl { get; set; }
    public decimal x_je { get; set; }
    public decimal x_bcg_sl { get; set; }
    public string x_hzjb { get; set; } = string.Empty;
}
