using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface IAuthService
{
    /// <summary>
    /// 验证用户名和密码，成功返回 LoginUser，失败返回 null
    /// </summary>
    Task<LoginUser?> ValidateAsync(string userName, string password);
}
