using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

/// <summary>
/// 通用项字典服务：三级标准补充项的 CRUD、启用/停用与排序(含理由审计)。
/// </summary>
public interface IElementDictService
{
    /// <summary>按级别获取字典项(按 SortOrder 升序)。includeDisabled=false 时仅返回启用项。</summary>
    Task<IReadOnlyList<ElementDictDto>> GetByLevelAsync(byte level, bool includeDisabled);

    /// <summary>新增字典项，返回新 Id。SortOrder 默认追加到同级末尾。</summary>
    Task<int> CreateAsync(ElementDictDto dto, string userName);

    /// <summary>修改字典项(名称/x_lx/规格/单位/挂载分类/备注等)。</summary>
    Task<(bool Success, string Message)> UpdateAsync(ElementDictDto dto, string userName);

    /// <summary>启用/停用字典项。</summary>
    Task<(bool Success, string Message)> ToggleEnableAsync(int id, bool enabled, string userName);

    /// <summary>
    /// 调整同级顺序。orderedIds 为该级全部项的目标顺序。
    /// 规则：理由必填；锁定项(器件)不可移出首位；写审计日志。
    /// </summary>
    Task<(bool Success, string Message)> ReorderAsync(byte level, IReadOnlyList<int> orderedIds, string reason, string userName);

    /// <summary>
    /// 获取导入时默认写入的第2级节点(IsDefaultOnImport=1 且 IsEnabled=1，按 SortOrder)。
    /// 供 SavePlan 生成控制柜下固定分类节点使用。
    /// </summary>
    Task<IReadOnlyList<(string Name, int Xlx)>> GetDefaultImportLevel2Async();
}
