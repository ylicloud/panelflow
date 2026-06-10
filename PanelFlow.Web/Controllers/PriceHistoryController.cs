using Microsoft.AspNetCore.Mvc;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Core.Services;
using PanelFlow.Web.Extensions;
using PanelFlow.Web.Filters;

namespace PanelFlow.Web.Controllers;

/// <summary>
/// 历史价格维护：浏览聚合价格、下钻来源、剔除异常、重新生成历史价格。
/// </summary>
[RoleAuthorize(RoleNames.Admin, RoleNames.Quoter)]
public class PriceHistoryController : Controller
{
    private readonly IPriceHistoryService _service;

    public PriceHistoryController(IPriceHistoryService service)
    {
        _service = service;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "历史价格维护";
        ViewData["BreadcrumbTitle"] = "历史价格维护";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> List(
        string? keyword, bool onlySuspect = false, int page = 1, int pageSize = 30,
        string? sortBy = "ggxh", bool sortAsc = true)
    {
        var result = await _service.ListHistoryAsync(keyword, onlySuspect, page, pageSize, sortBy, sortAsc);
        return Json(new { success = true, result });
    }

    [HttpGet]
    public async Task<IActionResult> SourceRows(string xWzdh)
    {
        if (string.IsNullOrWhiteSpace(xWzdh))
        {
            return Json(new { success = false, message = "型号指纹不能为空" });
        }

        var items = await _service.GetSourceRowsAsync(xWzdh);
        return Json(new { success = true, items });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Exclude(string fabh, string? xWzdh, string reason)
    {
        var (success, message) = await _service.AddExclusionAsync(fabh, xWzdh, reason, CurrentUserName());
        return Json(new { success, message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveExclusion(int id)
    {
        var (success, message) = await _service.RemoveExclusionAsync(id, CurrentUserName());
        return Json(new { success, message });
    }

    [HttpGet]
    public async Task<IActionResult> Exclusions()
    {
        var items = await _service.ListExclusionsAsync();
        return Json(new { success = true, items });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh()
    {
        var (success, message) = await _service.RefreshHistoryAsync(CurrentUserName());
        return Json(new { success, message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAttributes([FromBody] List<PriceHistoryAttributeUpdateItem> items)
    {
        var (success, message) = await _service.UpdateAttributesAsync(items ?? [], CurrentUserName());
        return Json(new { success, message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchUpdateAttributes([FromBody] PriceHistoryBatchUpdateRequest request)
    {
        var (success, message, affectedCount) = await _service.BatchUpdateAttributesAsync(
            request ?? new PriceHistoryBatchUpdateRequest(), CurrentUserName());
        return Json(new { success, message, affectedCount });
    }

    private string CurrentUserName() =>
        HttpContext.Session.GetLoginUser()?.UserName?.Trim() ?? string.Empty;
}
