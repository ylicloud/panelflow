namespace PanelFlow.Core.Services;

/// <summary>
/// YHQXGL.js 字段整数值与系统角色名称的映射。
/// 0~5 与旧系统 UserRole 枚举对齐，6~7 为新增角色。
/// </summary>
public static class RoleMapper
{
    private static readonly Dictionary<int, string> _map = new()
    {
        { 0, RoleNames.Admin },              // SuperAdmin
        { 1, RoleNames.Viewer },             // Viewer（只读）
        { 2, RoleNames.Quoter },             // QuotationStaff
        { 3, RoleNames.Purchaser },          // PurchasingStaff
        { 4, RoleNames.QualityInspector },   // QualityInspector
        { 5, RoleNames.Assembler },          // Installer → 装配人员
        { 6, RoleNames.ProductionManager },  // 新增：生产管理人员
        { 7, RoleNames.Warehouse },          // 新增：仓库人员
    };

    public static string GetRoleName(int js)
    {
        return _map.TryGetValue(js, out var name) ? name : RoleNames.Viewer;
    }

    public static int GetRoleId(string roleName)
    {
        foreach (var kv in _map)
        {
            if (kv.Value == roleName) return kv.Key;
        }
        return 1;
    }

    public static Dictionary<int, string> GetAllRoles() => new(_map);
}
