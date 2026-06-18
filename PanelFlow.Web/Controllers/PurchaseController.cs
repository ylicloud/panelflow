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

/// <summary>采购管理 - 已下达采购计划（验证、打印）</summary>
[RoleAuthorize(RoleNames.Admin, RoleNames.Purchaser)]
public class PurchaseController : Controller
{
    private readonly IPurchaseService _purchaseService;

    public PurchaseController(IPurchaseService purchaseService)
    {
        _purchaseService = purchaseService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, int page = 1, int pageSize = 30)
    {
        ViewData["Title"] = "采购计划执行";
        ViewData["BreadcrumbTitle"] = "采购计划执行";

        var result = await _purchaseService.GetPlanListAsync(keyword, null, page, pageSize, issuedOnly: true);
        var loginUser = HttpContext.Session.GetLoginUser();
        return View(new PurchasePlanListViewModel
        {
            Keyword = keyword?.Trim(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            TotalPages = result.TotalPages,
            IssuedOnly = true,
            Items = result.Items,
            CurrentUserName = loginUser?.UserName ?? string.Empty
        });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var plan = await _purchaseService.GetPlanByIdAsync(id);
        if (plan == null || plan.Status < PurchasePlanStatus.Issued)
        {
            TempData["ErrorMessage"] = "采购计划不存在或尚未下达。";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = $"采购验证 {plan.PlanNo}";
        ViewData["BreadcrumbTitle"] = "采购验证";
        return View(new PurchasePlanEditViewModel
        {
            Plan = plan,
            CanEdit = false,
            CanVerify = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveVerification(int id, [FromBody] List<PurchasePlanItemDto> items)
    {
        var (success, message) = await _purchaseService.SaveVerificationAsync(id, items);
        return Json(new { success, message });
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
