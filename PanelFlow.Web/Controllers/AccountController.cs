using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Web.Extensions;
using PanelFlow.Web.Filters;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace PanelFlow.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;
    private readonly IPasswordCryptoService _cryptoService;
    private readonly ApplicationDbContext _db;
    private readonly IAuditLogService _auditLogService;

    public AccountController(
        IAuthService authService,
        IPasswordCryptoService cryptoService,
        ApplicationDbContext db,
        IAuditLogService auditLogService)
    {
        _authService = authService;
        _cryptoService = cryptoService;
        _db = db;
        _auditLogService = auditLogService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (HttpContext.Session.GetLoginUser() != null)
            return RedirectToAction("Index", "Home");

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> DbStatus()
    {
        var (connected, error) = await CheckDbConnectionAsync();
        return Json(new { connected, error });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl)
    {
        if (!ModelState.IsValid)
            return View(model);

        PanelFlow.Core.Models.LoginUser? user;
        try
        {
            user = await _authService.ValidateAsync(model.UserName, model.Password);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, "无法连接数据库，请联系管理员。");
            return View(model);
        }

        if (user == null)
        {
            await _auditLogService.WriteAsync(new()
            {
                ActionType = "Login",
                Module = "Account",
                EntityName = "SysUser",
                EntityId = model.UserName?.Trim(),
                UserName = (model.UserName ?? string.Empty).Trim(),
                ClientIp = GetClientIp(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                IsSuccess = false,
                ErrorMessage = "用户名或密码错误",
                AfterData = JsonSerializer.Serialize(new { UserName = model.UserName?.Trim() })
            });

            ModelState.AddModelError(string.Empty, "用户名或密码错误");
            return View(model);
        }

        HttpContext.Session.SetLoginUser(user);

        await _auditLogService.WriteAsync(new()
        {
            ActionType = "Login",
            Module = "Account",
            EntityName = "SysUser",
            EntityId = user.UserName,
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            RoleName = user.RoleName,
            ClientIp = GetClientIp(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            IsSuccess = true,
            AfterData = JsonSerializer.Serialize(new
            {
                user.UserName,
                user.DisplayName,
                user.RoleName
            })
        });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [HttpGet]
    [RoleAuthorize]
    public IActionResult ChangePassword()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RoleAuthorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var loginUser = HttpContext.Session.GetLoginUser();
        if (loginUser == null)
            return RedirectToAction("Login");

        var user = await _db.SysUsers
            .FirstOrDefaultAsync(u => u.yhmcc == loginUser.UserName);

        if (user == null)
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // 验证旧密码
        DateTime seed = user.kzhdrq ?? DateTime.Now;
        if (!_cryptoService.Verify(model.OldPassword.Trim(), user.kl.Trim(), seed))
        {
            ModelState.AddModelError("OldPassword", "当前密码不正确");
            return View(model);
        }

        // 修改密码：生成新种子（精确到分钟，秒归零）
        var now = DateTime.Now;
        var newSeed = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

        user.kl = _cryptoService.Encrypt(model.NewPassword.Trim(), newSeed);
        user.kzhdrq = newSeed;

        _db.SysUsers.Update(user);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "密码修改成功，请记住新密码。";
        return RedirectToAction("ChangePassword");
    }

    private string? GetClientIp()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private async Task<(bool connected, string? error)> CheckDbConnectionAsync()
    {
        try
        {
            var connStr = _db.Database.GetConnectionString() ?? string.Empty;
            var builder = new SqlConnectionStringBuilder(connStr) { ConnectTimeout = 6 };
            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            return (true, null);
        }
        catch (SqlException ex)
        {
            return (false, $"SQL-{ex.Number}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}

public class LoginViewModel
{
    [Required(ErrorMessage = "请输入用户名")]
    [Display(Name = "用户名")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入密码")]
    [DataType(DataType.Password)]
    [Display(Name = "密码")]
    public string Password { get; set; } = string.Empty;
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "请输入当前密码")]
    [DataType(DataType.Password)]
    [Display(Name = "当前密码")]
    public string OldPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入新密码")]
    [StringLength(20, MinimumLength = 6, ErrorMessage = "密码长度必须在 6-20 位之间")]
    [DataType(DataType.Password)]
    [Display(Name = "新密码")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "请确认新密码")]
    [DataType(DataType.Password)]
    [Display(Name = "确认新密码")]
    [Compare("NewPassword", ErrorMessage = "两次输入的密码不一致")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
