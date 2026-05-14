namespace PanelFlow.Core.Models;

/// <summary>
/// 客户联系人（KHYLB_CONTACT）DTO。
/// </summary>
public class CustomerContactDto
{
    public int Id { get; set; }
    public string CompanyNo { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public int SortNo { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
