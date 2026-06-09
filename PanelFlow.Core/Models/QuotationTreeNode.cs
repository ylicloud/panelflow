namespace PanelFlow.Core.Models;

/// <summary>
/// 报价单结构树运行时节点（内存构树，不持久化）。
/// </summary>
public class QuotationTreeNode
{
    public string Xbm { get; set; } = string.Empty;

    /// <summary>重编码后的新编码；写回前由 TreeReencode 填充。</summary>
    public string NewXbm { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public int Xlx { get; set; }

    public int Level => string.IsNullOrEmpty(Xbm) ? 0 : Xbm.Trim().Length / 4;

    /// <summary>是否本次操作新挂入的节点。</summary>
    public bool IsNew { get; set; }

    /// <summary>来源字典 SortOrder；用于挂入后同级排序，进而决定重编码后的 x_bm。</summary>
    public int DictSortOrder { get; set; }

    public BjbRowSnapshot Data { get; set; } = new();
    public List<QuotationTreeNode> Children { get; set; } = [];

    /// <summary>第2级器件节点（x_lx=1），锁定不可删、不可移出首位。</summary>
    public bool IsLockedDevice => Level == 2 && Xlx == 1;

    public string EffectiveCode => string.IsNullOrWhiteSpace(NewXbm) ? Xbm.Trim() : NewXbm.Trim();
}
