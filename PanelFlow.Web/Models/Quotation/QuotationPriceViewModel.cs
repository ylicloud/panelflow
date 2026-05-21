using PanelFlow.Web.Controllers;

namespace PanelFlow.Web.Models.Quotation;

public class QuotationPriceViewModel
{
    public string QuotationNo { get; set; } = string.Empty;
    public string QuotationName { get; set; } = string.Empty;
    public int CurrentStatus { get; set; }
    public string ActiveSection { get; set; } = PriceSection.ImportComponents;
    public List<QuotationTreeNodeViewModel> TreeNodes { get; set; } = [];
}
