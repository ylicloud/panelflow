namespace PanelFlow.Core.Models;

/// <summary>
/// 客户信息（KHYLB）列表展示 DTO。
/// </summary>
public class CustomerDto
{
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyNo { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
