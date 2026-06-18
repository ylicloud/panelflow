namespace PanelFlow.Core.Models;

/// <summary>批量价格查询请求体。</summary>
public class PriceBatchQueryRequest
{
    public List<string>? Specs { get; set; }
    public List<string>? WzdhList { get; set; }
}
