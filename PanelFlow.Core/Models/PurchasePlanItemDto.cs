namespace PanelFlow.Core.Models;

public class PurchasePlanItemDto
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int SortNo { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string ItemSpec { get; set; } = string.Empty;
    public string? ItemUnit { get; set; }
    public decimal ItemQty { get; set; }
    public decimal ItemNoBuyQty { get; set; }
    public string? ItemManufacturer { get; set; }
    public short ChangeType { get; set; }
    public string? ChangeRemark { get; set; }
    public DateTime? NeedDate { get; set; }
    public string? Remark { get; set; }
    public bool? HasCert { get; set; }
    public bool? HasInspection { get; set; }
    public bool? AppearanceOk { get; set; }
    public bool? HasAccessories { get; set; }
    public bool? HasDocuments { get; set; }
    public DateTime? VerifyDate { get; set; }
    public string? Conclusion { get; set; }
    public string? Verifier { get; set; }
    public DateTime? VerifiedAt { get; set; }

    public string DisplayRemark =>
        !string.IsNullOrWhiteSpace(ChangeRemark) ? ChangeRemark!.Trim() : Remark?.Trim() ?? string.Empty;
}
