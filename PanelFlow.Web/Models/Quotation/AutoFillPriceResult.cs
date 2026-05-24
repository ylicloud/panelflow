namespace PanelFlow.Web.Models.Quotation;

public class AutoFillPriceResult
{
    public bool Success { get; set; }
    public int Matched { get; set; }
    public int Updated { get; set; }
    public int Unmatched { get; set; }
    public int Total { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, PriceInfo> Prices { get; set; } = new();
}
