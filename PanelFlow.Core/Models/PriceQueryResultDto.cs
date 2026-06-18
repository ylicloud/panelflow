namespace PanelFlow.Core.Models;

/// <summary>元件价格查询 API 单条结果。</summary>
public class PriceQueryResultDto
{
    public bool Found { get; set; }
    public string? InputSpec { get; set; }
    public string XWzdh { get; set; } = string.Empty;
    public string? Ggxh { get; set; }
    public string? XMc { get; set; }
    public string? XDw { get; set; }
    public string? XSccj { get; set; }
    public decimal? LastPrice { get; set; }
    public string? LastFabh { get; set; }
    public DateTime? LastDate { get; set; }
    public decimal? AvgPrice { get; set; }
    public int? AvgCount { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Message { get; set; }
}

/// <summary>批量查询响应。</summary>
public class PriceBatchQueryResultDto
{
    public int Total { get; set; }
    public int FoundCount { get; set; }
    public IReadOnlyList<PriceQueryResultDto> Items { get; set; } = [];
}
