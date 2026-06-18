using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;

namespace PanelFlow.Core.Services;

/// <summary>
/// 基于静态配置的角色菜单权限服务。
/// 菜单结构按业务流程顺序排列，后期可改为数据库驱动。
/// </summary>
public class PermissionService : IPermissionService
{
    private static readonly List<MenuItemModel> _allMenus = BuildAllMenus();

    public List<MenuItemModel> GetMenusForRole(string roleName)
    {
        return FilterMenusForRole(_allMenus, roleName);
    }

    public bool HasAccess(string roleName, string controller, string action)
    {
        // 管理员拥有全部权限
        if (roleName == RoleNames.Admin)
            return true;

        return CheckAccess(_allMenus, roleName, controller, action);
    }

    private static List<MenuItemModel> FilterMenusForRole(
        List<MenuItemModel> menus, string roleName)
    {
        var result = new List<MenuItemModel>();
        foreach (var menu in menus)
        {
            if (!CanAccess(menu, roleName))
                continue;

            var filteredChildren = FilterMenusForRole(menu.Children, roleName);
            if (menu.Url == null && filteredChildren.Count == 0)
                continue;

            var filtered = new MenuItemModel
            {
                Title = menu.Title,
                Icon = menu.Icon,
                Url = menu.Url,
                AllowedRoles = menu.AllowedRoles,
                Children = filteredChildren
            };
            result.Add(filtered);
        }
        return result;
    }

    private static bool CanAccess(MenuItemModel menu, string roleName)
    {
        if (roleName == RoleNames.Admin) return true;
        if (menu.AllowedRoles.Count == 0) return true;
        return menu.AllowedRoles.Contains(roleName);
    }

    private static bool CheckAccess(
        List<MenuItemModel> menus, string roleName, string controller, string action)
    {
        foreach (var menu in menus)
        {
            if (menu.Url != null && menu.Url.Contains($"/{controller}/{action}",
                StringComparison.OrdinalIgnoreCase))
            {
                return CanAccess(menu, roleName);
            }
            if (CheckAccess(menu.Children, roleName, controller, action))
                return true;
        }
        return false;
    }

    private static List<MenuItemModel> BuildAllMenus()
    {
        var admin = new[] { RoleNames.Admin };
        var quoterRoles = new[] { RoleNames.Admin, RoleNames.Quoter, RoleNames.ProductionManager };
        var priceHistoryRoles = new[] { RoleNames.Admin, RoleNames.Quoter };
        var contractRoles = new[] { RoleNames.Admin, RoleNames.Quoter };
        var productionRoles = new[] { RoleNames.Admin, RoleNames.ProductionManager };
        var purchaseRoles = new[] { RoleNames.Admin, RoleNames.Purchaser };
        var warehouseRoles = new[] { RoleNames.Admin, RoleNames.Warehouse };
        var assemblyRoles = new[] { RoleNames.Admin, RoleNames.Assembler };
        var qcRoles = new[] { RoleNames.Admin, RoleNames.QualityInspector };
        var shipRoles = new[] { RoleNames.Admin, RoleNames.ProductionManager };

        return
        [
            new MenuItemModel
            {
                Title = "项目管理",
                Icon = "bi-folder2-open",
                AllowedRoles = [.. quoterRoles],
                Children =
                [
                    new() { Title = "报价单", Icon = "bi-file-text", Url = "/Quotation/Index",
                        AllowedRoles = [.. quoterRoles] },
                    new() { Title = "报价单结构维护", Icon = "bi-diagram-3", Url = "/Quotation/StructureMaintain",
                        AllowedRoles = [.. quoterRoles] },
                    new() { Title = "Excel合并", Icon = "bi-files", Url = "/Quotation/MergeExcel",
                        AllowedRoles = [.. quoterRoles] },
                    new() { Title = "制造合同", Icon = "bi-file-earmark-check", Url = "/Contract/Index",
                        AllowedRoles = [.. contractRoles] },
                    new MenuItemModel
                    {
                        Title = "基础数据",
                        Icon = "bi-database",
                        AllowedRoles = [.. quoterRoles],
                        Children =
                        [
                            new() { Title = "客户", Icon = "bi-buildings", Url = "/Customer/Index",
                                AllowedRoles = [.. quoterRoles] },
                            new() { Title = "通用项字典", Icon = "bi-collection", Url = "/ElementDict/Index",
                                AllowedRoles = [.. quoterRoles] },
                            new() { Title = "历史价格维护", Icon = "bi-cash-stack", Url = "/PriceHistory/Index",
                                AllowedRoles = [.. priceHistoryRoles] },
                        ]
                    },
                ]
            },
            new MenuItemModel
            {
                Title = "生产管理",
                Icon = "bi-gear",
                AllowedRoles = [.. productionRoles],
                Children =
                [
                    new() { Title = "生产任务单", Icon = "bi-list-task", Url = "/Production/Index",
                        AllowedRoles = [.. productionRoles] },
                    new() { Title = "采购计划", Icon = "bi-clipboard-data", Url = "/PurchasePlan/Index",
                        AllowedRoles = [.. productionRoles] },
                ]
            },
            new MenuItemModel
            {
                Title = "采购管理",
                Icon = "bi-cart3",
                AllowedRoles = [.. purchaseRoles],
                Children =
                [
                    new() { Title = "采购计划执行", Icon = "bi-bag", Url = "/Purchase/Index",
                        AllowedRoles = [.. purchaseRoles] },
                    new() { Title = "到货验收", Icon = "bi-box-seam", Url = "/Receipt/Index",
                        AllowedRoles = [.. purchaseRoles] },
                ]
            },
            new MenuItemModel
            {
                Title = "库存管理",
                Icon = "bi-archive",
                AllowedRoles = [.. warehouseRoles],
                Children =
                [
                    new() { Title = "库存查询", Icon = "bi-search", Url = "/Inventory/Index",
                        AllowedRoles = [.. warehouseRoles] },
                    new() { Title = "入库单", Icon = "bi-box-arrow-in-down", Url = "/Inventory/StockIn",
                        AllowedRoles = [.. warehouseRoles] },
                    new() { Title = "出库单", Icon = "bi-box-arrow-up", Url = "/Inventory/StockOut",
                        AllowedRoles = [.. warehouseRoles] },
                    new() { Title = "盘点", Icon = "bi-clipboard-check", Url = "/Inventory/Stocktake",
                        AllowedRoles = [.. warehouseRoles] },
                ]
            },
            new MenuItemModel
            {
                Title = "装配管理",
                Icon = "bi-tools",
                AllowedRoles = [.. assemblyRoles],
                Children =
                [
                    new() { Title = "装配任务", Icon = "bi-list-check", Url = "/Assembly/Index",
                        AllowedRoles = [.. assemblyRoles] },
                    new() { Title = "装配进度", Icon = "bi-bar-chart-steps", Url = "/Assembly/Progress",
                        AllowedRoles = [.. assemblyRoles] },
                ]
            },
            new MenuItemModel
            {
                Title = "质检管理",
                Icon = "bi-patch-check",
                AllowedRoles = [.. qcRoles],
                Children =
                [
                    new() { Title = "质检任务", Icon = "bi-clipboard2-pulse", Url = "/QualityCheck/Index",
                        AllowedRoles = [.. qcRoles] },
                    new() { Title = "不合格品处理", Icon = "bi-exclamation-triangle", Url = "/QualityCheck/Reject",
                        AllowedRoles = [.. qcRoles] },
                ]
            },
            new MenuItemModel
            {
                Title = "发货管理",
                Icon = "bi-truck",
                AllowedRoles = [.. shipRoles],
                Children =
                [
                    new() { Title = "发货单", Icon = "bi-send", Url = "/Shipping/Index",
                        AllowedRoles = [.. shipRoles] },
                    new() { Title = "发运记录", Icon = "bi-journal-text", Url = "/Shipping/Records",
                        AllowedRoles = [.. shipRoles] },
                ]
            },
            new MenuItemModel
            {
                Title = "系统管理",
                Icon = "bi-person-gear",
                AllowedRoles = [.. admin],
                Children =
                [
                    new() { Title = "用户管理", Icon = "bi-people", Url = "/SysAdmin/Users",
                        AllowedRoles = [.. admin] },
                    new() { Title = "审计日志", Icon = "bi-journal-text", Url = "/SysAdmin/AuditLogs",
                        AllowedRoles = [.. admin] },
                    new() { Title = "角色配置", Icon = "bi-shield-lock", Url = "/SysAdmin/Roles",
                        AllowedRoles = [.. admin] },
                ]
            },
        ];
    }
}
