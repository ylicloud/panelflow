using System.ComponentModel.DataAnnotations;

namespace PanelFlow.Core.Models;

public class StructureApplyRequest
{
    [Required]
    public string Fabh { get; set; } = string.Empty;

    public List<StructureOperation> Operations { get; set; } = [];
}

/// <summary>
/// 结构写操作：AddLevel2 / AddLevel3 / Delete / Rename / ReorderSiblings
/// </summary>
public class StructureOperation
{
    public string Type { get; set; } = string.Empty;
    public List<string> TargetCodes { get; set; } = [];
    public List<int> DictIds { get; set; } = [];
    public string? NewName { get; set; }

    /// <summary>同级重排时的目标顺序（x_bm 列表）。</summary>
    public List<string>? OrderedCodes { get; set; }

    /// <summary>重排时的父节点编码；第1级重排时为空。</summary>
    public string? ParentCode { get; set; }

    public string? Reason { get; set; }
}

public class StructureApplyResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int AddedCount { get; set; }
    public int SkippedCount { get; set; }
    public int DeletedCount { get; set; }
    public int RenamedCount { get; set; }
    public int ReorderedCount { get; set; }
    public int TotalRowsWritten { get; set; }
}
