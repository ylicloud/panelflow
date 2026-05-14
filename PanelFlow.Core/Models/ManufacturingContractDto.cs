namespace PanelFlow.Core.Models;

/// <summary>
/// 制造合同（XMYLB）展示与编辑 DTO。
/// </summary>
public class ManufacturingContractDto
{
    public string ContractNo { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string LegacyContractNo { get; set; } = string.Empty;
    public DateTime? SignDate { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string ContractContent { get; set; } = string.Empty;
    public DateTime? DeliveryDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string CustomerNo { get; set; } = string.Empty;
    public string SignCompany { get; set; } = string.Empty;
    public string QuotationPlanNo { get; set; } = string.Empty;
    public int CurrentStatus { get; set; }
    public string Remark { get; set; } = string.Empty;
}
