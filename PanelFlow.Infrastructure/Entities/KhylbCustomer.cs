namespace PanelFlow.Infrastructure.Entities;

public class KhylbCustomer
{
    public string gsbh { get; set; } = string.Empty;
    public string gsmc { get; set; } = string.Empty;
    public string gsld { get; set; } = string.Empty;
    public string lxr { get; set; } = string.Empty;
    public string lxdh { get; set; } = string.Empty;
    public DateTime? created_at { get; set; }
    public DateTime? updated_at { get; set; }
    public string beizhu { get; set; } = string.Empty;
}
