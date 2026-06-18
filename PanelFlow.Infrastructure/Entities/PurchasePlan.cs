namespace PanelFlow.Infrastructure.Entities;

/// <summary>采购计划单头（PF_PURCHASE_PLAN）</summary>
public class PurchasePlan
{
    public int Id { get; set; }
    public string PlanNo { get; set; } = string.Empty;
    public string Fabh { get; set; } = string.Empty;
    public string? ContractNo { get; set; }
    public short Status { get; set; }
    public string Creator { get; set; } = string.Empty;
    public string? Reviewer { get; set; }
    public string? UnitChief { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? IssuedAt { get; set; }
    public string? IssuedBy { get; set; }

    public ICollection<PurchasePlanItem> Items { get; set; } = new List<PurchasePlanItem>();
}
