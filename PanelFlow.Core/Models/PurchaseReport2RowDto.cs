namespace PanelFlow.Core.Models;

public class PurchaseReport2RowDto
{
    public int Seq { get; set; }
    public string ContractNo { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ItemSpec { get; set; } = string.Empty;
    public decimal ItemQty { get; set; }
    public string ItemManufacturer { get; set; } = string.Empty;
    public string HasCertText { get; set; } = string.Empty;
    public string HasInspectionText { get; set; } = string.Empty;
    public string AppearanceText { get; set; } = string.Empty;
    public string HasAccessoriesText { get; set; } = string.Empty;
    public string HasDocumentsText { get; set; } = string.Empty;
    public string? VerifyDateText { get; set; }
    public string Conclusion { get; set; } = string.Empty;
    public string Verifier { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}

public class PurchaseReport2DataDto
{
    public string PlanNo { get; set; } = string.Empty;
    public List<PurchaseReport2RowDto> Rows { get; set; } = new();
}
