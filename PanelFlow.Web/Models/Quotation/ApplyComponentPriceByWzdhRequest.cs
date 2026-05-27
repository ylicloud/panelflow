namespace PanelFlow.Web.Models.Quotation;

/// <summary>
/// 将单价同步到本报价单内多条元件行。
/// 优先使用 <see cref="Codes"/>（根节点汇总 matchKey 解析）；否则按 <see cref="Wzdh"/> 匹配。
/// </summary>
public sealed class ApplyComponentPriceByWzdhRequest
{
    public string Wzdh { get; set; } = string.Empty;
    public decimal NewPrice { get; set; }
    /// <summary>显式指定 x_bm 列表时，按编码精确更新（用于根节点汇总分组）。</summary>
    public List<string> Codes { get; set; } = new();
}
