namespace PanelFlow.Core.Services;

/// <summary>
/// 系统角色名称常量。
/// 与 YHQXGL.js 整数值的映射在 RoleMapper 中定义。
/// </summary>
public static class RoleNames
{
    public const string Admin = "管理员";
    public const string Viewer = "普通用户";
    public const string Quoter = "报价员";
    public const string Purchaser = "采购人员";
    public const string QualityInspector = "质检人员";
    public const string Assembler = "装配人员";
    public const string ProductionManager = "生产管理人员";
    public const string Warehouse = "仓库人员";
}
