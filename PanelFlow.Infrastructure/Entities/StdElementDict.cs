namespace PanelFlow.Infrastructure.Entities;

/// <summary>
/// 通用项字典：报价单三级(控制柜/单元属性/元件)的标准补充项主数据。
/// 新增表 STD_ELEMENT_DICT，不影响历史 PB 表。
/// </summary>
public class StdElementDict
{
    public int Id { get; set; }

    /// <summary>层级：1/2/3，对应 x_bm 的 4/8/12 位。</summary>
    public byte Level { get; set; }

    /// <summary>名称，写入 BJB.x_mc。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>类型，写入 BJB.x_lx；对齐汇总槽位的稳定身份。</summary>
    public int Xlx { get; set; }

    /// <summary>默认数量，挂入时写入 BJB.x_sl。</summary>
    public decimal Amount { get; set; } = 1m;

    /// <summary>规格型号，挂入时写入 BJB.x_ggxh，空则不写。</summary>
    public string? Ggxh { get; set; }

    /// <summary>默认单位，写入 BJB.x_dw。</summary>
    public string? DefaultUnit { get; set; }

    /// <summary>第3级专用：挂到哪个 8 位分类，默认 '0001'(器件)。</summary>
    public string? TargetParentScope { get; set; }

    /// <summary>同级排序，决定挂入时分配的位置编码。</summary>
    public int SortOrder { get; set; }

    /// <summary>导入控制柜时是否自动写入该第2级节点。</summary>
    public bool IsDefaultOnImport { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>锁定项(如器件)：不可删除、不可移出首位。</summary>
    public bool IsLocked { get; set; }

    public string? Remark { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}
