namespace PanelFlow.Core.Models;

/// <summary>
/// 存储在 Session 中的登录用户信息
/// </summary>
public class LoginUser
{
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>角色名称，如"管理员"、"报价员"等</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>历史系统用户类型编号（yhlx），用于映射角色</summary>
    public int UserType { get; set; }

    public string? DeptCode { get; set; }
}
