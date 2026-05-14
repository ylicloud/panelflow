using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Services;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Web.Extensions;
using PanelFlow.Web.Filters;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace PanelFlow.Web.Controllers;

[RoleAuthorize(RoleNames.Admin)]
public class SysAdminController : Controller
{
    private readonly IUserService _userService;
    private readonly IAuditLogService _auditLogService;
    private readonly ApplicationDbContext _db;

    public SysAdminController(
        IUserService userService,
        IAuditLogService auditLogService,
        ApplicationDbContext db)
    {
        _userService = userService;
        _auditLogService = auditLogService;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Users()
    {
        ViewData["BreadcrumbTitle"] = "用户管理";
        var users = await _userService.GetAllAsync();

        var list = users.Select(u => new UserListItemViewModel
        {
            Username = u.Username,
            DisplayName = u.DisplayName,
            RoleName = RoleMapper.GetRoleName(u.Role),
            IsEnabled = u.IsEnabled,
            LastLogin = u.LastLogin,
            Remark = u.Remark
        }).ToList();

        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        ViewData["BreadcrumbTitle"] = "编辑用户";
        var user = await _userService.GetByUsernameAsync(id);
        if (user == null)
            return RedirectToAction("Users");

        var model = new UserEditViewModel
        {
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsEnabled = user.IsEnabled,
            Remark = user.Remark
        };

        ViewBag.AllRoles = RoleMapper.GetAllRoles();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["BreadcrumbTitle"] = "编辑用户";
            ViewBag.AllRoles = RoleMapper.GetAllRoles();
            return View(model);
        }

        var loginUser = HttpContext.Session.GetLoginUser();
        var beforeUser = await _userService.GetByUsernameAsync(model.Username);
        var (success, message) = await _userService.UpdateAsync(
            model.Username, model.DisplayName, model.Role, model.IsEnabled, model.Remark ?? "");

        await _auditLogService.WriteAsync(new()
        {
            ActionType = "UpdateUser",
            Module = "SysAdmin",
            EntityName = "SysUser",
            EntityId = model.Username.Trim(),
            UserName = loginUser?.UserName ?? string.Empty,
            DisplayName = loginUser?.DisplayName,
            RoleName = loginUser?.RoleName,
            ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            IsSuccess = success,
            ErrorMessage = success ? null : message,
            BeforeData = beforeUser == null ? null : JsonSerializer.Serialize(new
            {
                beforeUser.Username,
                beforeUser.DisplayName,
                beforeUser.Role,
                beforeUser.IsEnabled,
                beforeUser.Remark
            }),
            AfterData = JsonSerializer.Serialize(new
            {
                Username = model.Username.Trim(),
                DisplayName = model.DisplayName.Trim(),
                model.Role,
                model.IsEnabled,
                Remark = model.Remark?.Trim() ?? string.Empty
            })
        });

        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            ViewData["BreadcrumbTitle"] = "编辑用户";
            ViewBag.AllRoles = RoleMapper.GetAllRoles();
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction("Users");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string username, string newPassword)
    {
        var (success, message) = await _userService.ResetPasswordAsync(username, newPassword);
        if (success)
            TempData["SuccessMessage"] = $"用户 \"{username.Trim()}\" {message}";
        else
            TempData["ErrorMessage"] = message;

        return RedirectToAction("Edit", new { id = username.Trim() });
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["BreadcrumbTitle"] = "新建用户";
        ViewBag.AllRoles = RoleMapper.GetAllRoles();
        return View(new UserCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["BreadcrumbTitle"] = "新建用户";
            ViewBag.AllRoles = RoleMapper.GetAllRoles();
            return View(model);
        }

        var (success, message) = await _userService.CreateAsync(
            model.Username, model.DisplayName, model.Role, model.Password, model.Remark ?? "");

        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            ViewData["BreadcrumbTitle"] = "新建用户";
            ViewBag.AllRoles = RoleMapper.GetAllRoles();
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction("Users");
    }

    [HttpGet]
    public async Task<IActionResult> AuditLogs(string? keyword, int? success, int page = 1)
    {
        ViewData["Title"] = "审计日志";
        ViewData["BreadcrumbTitle"] = "审计日志";

        const int pageSize = 50;
        if (page < 1) page = 1;

        var query = _db.SysAuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var q = keyword.Trim();
            query = query.Where(x =>
                x.UserName.Contains(q) ||
                (x.DisplayName != null && x.DisplayName.Contains(q)) ||
                x.ActionType.Contains(q) ||
                x.Module.Contains(q) ||
                (x.EntityId != null && x.EntityId.Contains(q)));
        }

        if (success.HasValue)
        {
            var isSuccess = success.Value == 1;
            query = query.Where(x => x.IsSuccess == isSuccess);
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        if (totalPages == 0) totalPages = 1;
        if (page > totalPages) page = totalPages;

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditLogItemViewModel
            {
                Id = x.Id,
                CreatedAt = x.CreatedAt,
                UserName = x.UserName,
                DisplayName = x.DisplayName,
                ActionType = x.ActionType,
                Module = x.Module,
                EntityName = x.EntityName,
                EntityId = x.EntityId,
                IsSuccess = x.IsSuccess,
                ErrorMessage = x.ErrorMessage,
                ClientIp = x.ClientIp
            })
            .ToListAsync();

        var model = new AuditLogListViewModel
        {
            Keyword = keyword?.Trim(),
            Success = success,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Items = items
        };

        return View(model);
    }
}

public class UserListItemViewModel
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime? LastLogin { get; set; }
    public string Remark { get; set; } = string.Empty;
}

public class UserEditViewModel
{
    [Display(Name = "登录名")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入姓名")]
    [StringLength(20, ErrorMessage = "姓名最长 20 个字符")]
    [Display(Name = "姓名")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "请选择角色")]
    [Display(Name = "角色")]
    public int Role { get; set; }

    [Display(Name = "启用")]
    public bool IsEnabled { get; set; } = true;

    [StringLength(100, ErrorMessage = "备注最长 100 个字符")]
    [Display(Name = "备注")]
    public string? Remark { get; set; }
}

public class UserCreateViewModel
{
    [Required(ErrorMessage = "请输入登录名")]
    [StringLength(10, ErrorMessage = "登录名最长 10 个字符")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "登录名仅允许字母、数字和下划线")]
    [Display(Name = "登录名")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入姓名")]
    [StringLength(20, ErrorMessage = "姓名最长 20 个字符")]
    [Display(Name = "姓名")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "请选择角色")]
    [Display(Name = "角色")]
    public int Role { get; set; } = 1;

    [Required(ErrorMessage = "请输入初始密码")]
    [StringLength(20, MinimumLength = 4, ErrorMessage = "密码长度 4-20 位")]
    [DataType(DataType.Password)]
    [Display(Name = "初始密码")]
    public string Password { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "备注最长 100 个字符")]
    [Display(Name = "备注")]
    public string? Remark { get; set; }
}

public class AuditLogListViewModel
{
    public string? Keyword { get; set; }
    public int? Success { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public List<AuditLogItemViewModel> Items { get; set; } = [];
}

public class AuditLogItemViewModel
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ClientIp { get; set; }
}
