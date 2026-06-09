using Microsoft.AspNetCore.Mvc;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Core.Services;
using PanelFlow.Web.Extensions;
using PanelFlow.Web.Filters;

namespace PanelFlow.Web.Controllers;

/// <summary>
/// 通用项字典维护：三级标准补充项的 CRUD、启用/停用、排序(含理由审计)。
/// </summary>
[RoleAuthorize(RoleNames.Admin, RoleNames.Quoter, RoleNames.ProductionManager)]
public class ElementDictController : Controller
{
    private readonly IElementDictService _service;

    public ElementDictController(IElementDictService service)
    {
        _service = service;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "通用项字典";
        ViewData["BreadcrumbTitle"] = "通用项字典";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetByLevel(byte level, bool includeDisabled = true)
    {
        if (level < 1 || level > 3)
        {
            return Json(new { success = false, message = "无效级别" });
        }

        var items = await _service.GetByLevelAsync(level, includeDisabled);
        return Json(new { success = true, items });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ElementDictDto dto)
    {
        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = FirstError() });
        }

        var id = await _service.CreateAsync(dto, CurrentUserName());
        return Json(new { success = true, message = "新增成功", id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(ElementDictDto dto)
    {
        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = FirstError() });
        }

        var (success, message) = await _service.UpdateAsync(dto, CurrentUserName());
        return Json(new { success, message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleEnable(int id, bool enabled)
    {
        var (success, message) = await _service.ToggleEnableAsync(id, enabled, CurrentUserName());
        return Json(new { success, message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder(byte level, [FromForm] List<int> orderedIds, string reason)
    {
        var (success, message) = await _service.ReorderAsync(level, orderedIds ?? [], reason, CurrentUserName());
        return Json(new { success, message });
    }

    private string CurrentUserName() =>
        HttpContext.Session.GetLoginUser()?.UserName?.Trim() ?? string.Empty;

    private string FirstError() =>
        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "输入有误";
}
