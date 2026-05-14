namespace PanelFlow.Core.Models;

/// <summary>
/// 导航菜单节点
/// </summary>
public class MenuItemModel
{
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string? Url { get; set; }

    /// <summary>有子菜单时展开，无子菜单时直接跳转</summary>
    public List<MenuItemModel> Children { get; set; } = [];

    /// <summary>允许访问此菜单的角色列表（空=全部允许）</summary>
    public List<string> AllowedRoles { get; set; } = [];
}
