namespace PanelFlow.Core.Models;

public class PurchaseReport1RowDto
{
    public int Seq { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string ItemSpec { get; set; } = string.Empty;
    public string ItemUnit { get; set; } = string.Empty;
    public decimal ItemQty { get; set; }
    public string? NeedDateText { get; set; }
    public string ItemManufacturer { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}

public class PurchaseReport1DataDto
{
    public string PlanNo { get; set; } = string.Empty;
    public string ContractName { get; set; } = string.Empty;
    public string ContractNo { get; set; } = string.Empty;
    public string Fabh { get; set; } = string.Empty;
    public List<PurchaseReport1RowDto> Rows { get; set; } = new();
}
