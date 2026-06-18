using PanelFlow.Core.Models;

namespace PanelFlow.Web.Models.Purchase;

public class PurchasePlanListViewModel
{
    public string? Keyword { get; set; }
    public short? StatusFilter { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool IssuedOnly { get; set; }
    public List<PurchasePlanDto> Items { get; set; } = new();
    public string CurrentUserName { get; set; } = string.Empty;
}

public class PurchasePlanCreateViewModel
{
    public string Fabh { get; set; } = string.Empty;
    public bool HasSummaryData { get; set; }
}

public class PurchasePlanEditViewModel
{
    public PurchasePlanDto Plan { get; set; } = new();
    public bool CanEdit { get; set; }
    public bool CanVerify { get; set; }
}
