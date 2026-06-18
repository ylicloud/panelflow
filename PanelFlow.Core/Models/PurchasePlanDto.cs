namespace PanelFlow.Core.Models;

public class PurchasePlanDto
{
    public int Id { get; set; }
    public string PlanNo { get; set; } = string.Empty;
    public string Fabh { get; set; } = string.Empty;
    public string? ContractNo { get; set; }
    public string? ContractName { get; set; }
    public short Status { get; set; }
    public string StatusText => Status switch
    {
        PurchasePlanStatus.Draft => "草稿",
        PurchasePlanStatus.Issued => "已下达",
        PurchasePlanStatus.Completed => "完成",
        _ => "未知"
    };
    public string Creator { get; set; } = string.Empty;
    public string? Reviewer { get; set; }
    public string? UnitChief { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? IssuedAt { get; set; }
    public string? IssuedBy { get; set; }
    public int ItemCount { get; set; }
    public List<PurchasePlanItemDto> Items { get; set; } = new();
}
