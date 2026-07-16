namespace PanelFlow.Core.Models;

public class QuotationSummaryPrecheckResult
{
    public bool HasEmptyComponent { get; set; }
    public string? EmptyComponentBm { get; set; }
}

public class QuotationSummaryRunResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Stage { get; set; }
}

public class QuotationSummaryStatusDto
{
    public bool HasHzb { get; set; }
    public bool HasXmyjb { get; set; }
    public bool HasXmyjhz { get; set; }
    public bool HasXmhz { get; set; }
    public int HzbCount { get; set; }
    public int XmyjbCount { get; set; }
    public int XmyjhzCount { get; set; }
    public int XmhzCount { get; set; }
}

public class QuotationSummaryPageDto
{
    public string Fabh { get; set; } = string.Empty;
    public string? QuotationName { get; set; }
    public QuotationSummaryStatusDto Status { get; set; } = new();
    public bool CanEdit { get; set; }
}
