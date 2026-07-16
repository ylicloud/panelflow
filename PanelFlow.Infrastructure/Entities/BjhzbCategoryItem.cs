namespace PanelFlow.Infrastructure.Entities;

/// <summary>报价汇总分类字典（BJHZB），标准分类框架，fabh=1。</summary>
public class BjhzbCategoryItem
{
    public decimal fabh { get; set; }
    public string x_bh { get; set; } = string.Empty;
    public string famc { get; set; } = string.Empty;
    public string x_mc { get; set; } = string.Empty;
    public string bz { get; set; } = string.Empty;
    public string x_flbh { get; set; } = string.Empty;
    public int x_fl_bhf { get; set; }
}
