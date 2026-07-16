namespace PanelFlow.Infrastructure.Entities;

/// <summary>项目元件按柜汇总表（BJB_XMYJB），PB 汇总输出。</summary>
public class BjbXmyjbItem
{
    public string fabh { get; set; } = string.Empty;
    public string x_dyh { get; set; } = string.Empty;
    public string x_ggxh { get; set; } = string.Empty;
    public string x_sccj { get; set; } = string.Empty;
    public string x_key_ry { get; set; } = string.Empty;
    public int x_lylx { get; set; }
    public string x_flbh { get; set; } = string.Empty;
    public string x_qjmc { get; set; } = string.Empty;
    public string x_dymc { get; set; } = string.Empty;
    public int x_lx { get; set; }
    public decimal x_zsl { get; set; }
    public decimal x_zje { get; set; }
    public decimal x_bcg_sl { get; set; }
    public decimal? x_sxh { get; set; }
    public decimal x_zxm_sl { get; set; }
    public decimal x_zxm_je { get; set; }
    public decimal x_zxm_bcg_sl { get; set; }
}
