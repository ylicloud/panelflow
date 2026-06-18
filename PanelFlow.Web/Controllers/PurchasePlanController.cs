using Microsoft.AspNetCore.Mvc;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Core.Services;
using PanelFlow.Infrastructure.Reports;
using PanelFlow.Web.Extensions;
using PanelFlow.Web.Filters;
using PanelFlow.Web.Models.Purchase;
using QuestPDF.Fluent;

namespace PanelFlow.Web.Controllers;

/// <summary>生产管理 - 采购计划（起草、编辑、下达）</summary>
[RoleAuthorize(RoleNames.Admin, RoleNames.ProductionManager)]
public class PurchasePlanController : Controller
{
    private readonly IPurchaseService _purchaseService;

    public PurchasePlanController(IPurchaseService purchaseService)
    {
        _purchaseService = purchaseService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, short? status, int page = 1, int pageSize = 30)
    {
        ViewData["Title"] = "采购计划";
        ViewData["BreadcrumbTitle"] = "采购计划";

        var result = await _purchaseService.GetPlanListAsync(keyword, status, page, pageSize);
        var loginUser = HttpContext.Session.GetLoginUser();
        return View(new PurchasePlanListViewModel
        {
            Keyword = keyword?.Trim(),
            StatusFilter = status,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            TotalPages = result.TotalPages,
            Items = result.Items,
            CurrentUserName = loginUser?.UserName ?? string.Empty
        });
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "新建采购计划";
        ViewData["BreadcrumbTitle"] = "新建采购计划";
        return View(new PurchasePlanCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PurchasePlanCreateViewModel model)
    {
        var loginUser = HttpContext.Session.GetLoginUser();
        if (loginUser == null)
            return RedirectToAction("Login", "Account");

        var (success, message, planId) = await _purchaseService.CreatePlanFromFabhAsync(
            model.Fabh, loginUser.UserName);

        if (!success)
        {
            TempData["ErrorMessage"] = message;
            ViewData["Title"] = "新建采购计划";
            ViewData["BreadcrumbTitle"] = "新建采购计划";
            model.HasSummaryData = await _purchaseService.HasSummaryDataAsync(model.Fabh);
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Edit), new { id = planId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var plan = await _purchaseService.GetPlanByIdAsync(id);
        if (plan == null)
        {
            TempData["ErrorMessage"] = "采购计划不存在。";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = $"编辑采购计划 {plan.PlanNo}";
        ViewData["BreadcrumbTitle"] = "编辑采购计划";
        return View(new PurchasePlanEditViewModel
        {
            Plan = plan,
            CanEdit = plan.Status == PurchasePlanStatus.Draft,
            CanVerify = false
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveItems(int id, [FromBody] List<PurchasePlanItemDto> items)
    {
        var (success, message) = await _purchaseService.SavePlanItemsAsync(id, items);
        return Json(new { success, message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(int id)
    {
        var loginUser = HttpContext.Session.GetLoginUser();
        if (loginUser == null)
            return RedirectToAction("Login", "Account");

        var (success, message) = await _purchaseService.IssuePlanAsync(id, loginUser.UserName);
        if (success)
            TempData["SuccessMessage"] = message;
        else
            TempData["ErrorMessage"] = message;

        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> CheckSummary(string fabh)
    {
        var hasData = await _purchaseService.HasSummaryDataAsync(fabh);
        return Json(new { hasData });
    }

    [HttpGet]
    public async Task<IActionResult> PrintReport1(int id, bool showDeleted = false)
    {
        var data = await _purchaseService.GetReport1DataAsync(id, showDeleted);
        if (data == null) return NotFound();

        var pdf = new PurchasePlanDocument(data).GeneratePdf();
        return File(pdf, "application/pdf", $"合同配套件采购计划表_{data.PlanNo}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> PrintReport2(int id, bool showDeleted = false)
    {
        var data = await _purchaseService.GetReport2DataAsync(id, showDeleted);
        if (data == null) return NotFound();

        var pdf = new PurchaseVerifyDocument(data).GeneratePdf();
        return File(pdf, "application/pdf", $"采购产品验证记录_{data.PlanNo}.pdf");
    }
}
