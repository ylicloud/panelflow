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

    /// <summary>当前路径是否匹配本节点或任意子孙菜单链接。</summary>
    public bool IsActiveForPath(string currentPath)
    {
        if (Url != null && currentPath.StartsWith(Url, StringComparison.OrdinalIgnoreCase))
            return true;
        return Children.Exists(c => c.IsActiveForPath(currentPath));
    }
}
