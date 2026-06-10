using System.ComponentModel.DataAnnotations;

namespace PanelFlow.Core.Models;

/// <summary>
/// 通用项字典 DTO，用于字典维护页与服务层交互。
/// </summary>
public class ElementDictDto
{
    public int Id { get; set; }

    [Range(1, 3, ErrorMessage = "级别必须为 1/2/3")]
    public byte Level { get; set; }

    [Required(ErrorMessage = "名称不能为空")]
    [StringLength(50, ErrorMessage = "名称最长 50 字")]
    public string Name { get; set; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "类型(x_lx)必须为非负整数")]
    public int Xlx { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "数量必须为非负数")]
    public decimal Amount { get; set; } = 1m;

    [StringLength(50, ErrorMessage = "规格型号最长 50 字")]
    public string? Ggxh { get; set; }

    [StringLength(10, ErrorMessage = "单位最长 10 字")]
    public string? DefaultUnit { get; set; }

    [StringLength(8, ErrorMessage = "挂载分类编码最长 8 位")]
    public string? TargetParentScope { get; set; }

    public int SortOrder { get; set; }

    public bool IsDefaultOnImport { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsLocked { get; set; }

    [StringLength(300, ErrorMessage = "备注最长 300 字")]
    public string? Remark { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}
