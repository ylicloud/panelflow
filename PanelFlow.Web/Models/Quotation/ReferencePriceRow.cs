namespace PanelFlow.Web.Models.Quotation;

public class ReferencePriceRow
{
    public decimal? LastPrice { get; set; }
    public decimal? AvgPrice { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int? AvgCount { get; set; }
}
