using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface IPermissionService
{
    /// <summary>
    /// 根据角色名称返回该角色可见的菜单列表
    /// </summary>
    List<MenuItemModel> GetMenusForRole(string roleName);

    /// <summary>
    /// 判断某角色是否有权限访问指定的 Controller/Action
    /// </summary>
    bool HasAccess(string roleName, string controller, string action);
}
