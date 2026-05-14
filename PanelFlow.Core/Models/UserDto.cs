namespace PanelFlow.Core.Models;

/// <summary>
/// 用户信息 DTO，用于 Service 层与 Controller 之间传递
/// </summary>
public class UserDto
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Role { get; set; }
    public bool IsEnabled { get; set; }
    public string Remark { get; set; } = string.Empty;
    public DateTime? LastLogin { get; set; }
}
