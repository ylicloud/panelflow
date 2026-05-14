namespace PanelFlow.Core.Models;

/// <summary>
/// 报价单（BJFAT）列表展示 DTO。
/// </summary>
public class QuotationDto
{
    public string QuotationNo { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public string QuotationName { get; set; } = string.Empty;
    public decimal? PlanModelNo { get; set; }
    public string Quoter { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string CustomerNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal PlanType { get; set; }
    public int CurrentStatus { get; set; }
}
