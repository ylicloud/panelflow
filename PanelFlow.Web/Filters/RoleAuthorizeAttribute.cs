using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PanelFlow.Core.Models;
using PanelFlow.Web.Extensions;

namespace PanelFlow.Web.Filters;

/// <summary>
/// 角色授权过滤器。
/// 用法：[RoleAuthorize("管理员", "采购人员")]
/// 未登录时跳转登录页；已登录但角色不符时返回 403 页面。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RoleAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _roles;

    /// <param name="roles">允许访问的角色，不传则只要求登录</param>
    public RoleAuthorizeAttribute(params string[] roles)
    {
        _roles = roles;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var loginUser = context.HttpContext.Session.GetLoginUser();

        if (loginUser == null)
        {
            var returnUrl = context.HttpContext.Request.Path;
            context.Result = new RedirectToActionResult("Login", "Account",
                new { returnUrl });
            return;
        }

        if (_roles.Length > 0 && !_roles.Contains(loginUser.RoleName))
        {
            context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
        }
    }
}
