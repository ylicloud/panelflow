namespace PanelFlow.Core.Models;

/// <summary>按当前筛选条件批量更新历史价格表单位/厂商。</summary>
public class PriceHistoryBatchUpdateRequest
{
    public string? Keyword { get; set; }
    public bool OnlySuspect { get; set; }
    public string? x_dw { get; set; }
    public string? x_sccj { get; set; }
}
