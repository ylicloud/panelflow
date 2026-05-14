namespace PanelFlow.Infrastructure.Entities;

public class KhylbCustomerContact
{
    public int Id { get; set; }
    public string gsbh { get; set; } = string.Empty;
    public string lxr { get; set; } = string.Empty;
    public string lxdh { get; set; } = string.Empty;
    public string email { get; set; } = string.Empty;
    public string zw { get; set; } = string.Empty;
    public bool is_default { get; set; }
    public int sort_no { get; set; }
    public bool is_enabled { get; set; }
    public DateTime? created_at { get; set; }
    public DateTime? updated_at { get; set; }
}
