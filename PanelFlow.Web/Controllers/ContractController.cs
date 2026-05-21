using Microsoft.AspNetCore.Mvc;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Core.Services;
using PanelFlow.Web.Extensions;
using PanelFlow.Web.Filters;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace PanelFlow.Web.Controllers;

[RoleAuthorize(RoleNames.Admin, RoleNames.Quoter)]
public class ContractController : Controller
{
    private readonly IManufacturingContractService _contractService;
    private readonly IAuditLogService _auditLogService;

    public ContractController(
        IManufacturingContractService contractService,
        IAuditLogService auditLogService)
    {
        _contractService = contractService;
        _auditLogService = auditLogService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, bool includeHistory = false, int page = 1, int pageSize = 30)
    {
        ViewData["Title"] = "制造合同";
        ViewData["BreadcrumbTitle"] = "制造合同";

        var result = await _contractService.GetListAsync(keyword, includeHistory, page, pageSize);
        var loginUser = HttpContext.Session.GetLoginUser();
        var model = new ContractListViewModel
        {
            Keyword = keyword?.Trim(),
            IncludeHistory = includeHistory,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            TotalPages = result.TotalPages,
            Items = result.Items.Select(ToListItem).ToList(),
            CurrentUserName = loginUser?.UserName ?? string.Empty
        };
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        ViewData["Title"] = "编辑制造合同";
        ViewData["BreadcrumbTitle"] = "编辑制造合同";

        var dto = await _contractService.GetByContractNoAsync(id);
        if (dto == null)
        {
            TempData["ErrorMessage"] = "合同不存在或已被删除";
            return RedirectToAction(nameof(Index));
        }

        return View(ToEditModel(dto));
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "录入制造合同";
        ViewData["BreadcrumbTitle"] = "录入制造合同";
        return View(new ContractCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ContractCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "录入制造合同";
            ViewData["BreadcrumbTitle"] = "录入制造合同";
            return View(model);
        }

        var dto = new ManufacturingContractDto
        {
            ContractNo = model.ContractNo,
            ProjectName = model.ProjectName,
            LegacyContractNo = model.LegacyContractNo ?? string.Empty,
            SignDate = model.SignDate,
            Owner = model.Owner,
            ContractContent = model.ContractContent,
            DeliveryDate = model.DeliveryDate,
            TotalAmount = model.TotalAmount,
            CustomerNo = model.CustomerNo ?? string.Empty,
            SignCompany = model.SignCompany ?? string.Empty,
            QuotationPlanNo = model.QuotationPlanNo ?? string.Empty,
            CurrentStatus = model.CurrentStatus,
            Remark = model.Remark ?? string.Empty
        };

        var (success, message) = await _contractService.CreateAsync(dto);
        await WriteAuditAsync("CreateContract", model.ContractNo, success, success ? null : message, null, dto);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            ViewData["Title"] = "录入制造合同";
            ViewData["BreadcrumbTitle"] = "录入制造合同";
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ContractEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "编辑制造合同";
            ViewData["BreadcrumbTitle"] = "编辑制造合同";
            return View(model);
        }

        var before = await _contractService.GetByContractNoAsync(model.ContractNo);
        var dto = new ManufacturingContractDto
        {
            ContractNo = model.ContractNo,
            ProjectName = model.ProjectName,
            SignDate = model.SignDate,
            Owner = model.Owner,
            ContractContent = model.ContractContent,
            DeliveryDate = model.DeliveryDate,
            TotalAmount = model.TotalAmount,
            CustomerNo = model.CustomerNo,
            SignCompany = model.SignCompany,
            CurrentStatus = model.CurrentStatus,
            Remark = model.Remark ?? string.Empty
        };

        var (success, message) = await _contractService.UpdateAsync(dto);
        await WriteAuditAsync("UpdateContract", model.ContractNo, success, success ? null : message, before, dto);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            ViewData["Title"] = "编辑制造合同";
            ViewData["BreadcrumbTitle"] = "编辑制造合同";
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var before = await _contractService.GetByContractNoAsync(id);
        var (success, message) = await _contractService.DeleteAsync(id);
        await WriteAuditAsync("DeleteContract", id, success, success ? null : message, before, null);

        if (success)
            TempData["SuccessMessage"] = $"合同 {id.Trim()} 已删除";
        else
            TempData["ErrorMessage"] = message;

        return RedirectToAction(nameof(Index));
    }

    private async Task WriteAuditAsync(
        string actionType,
        string entityId,
        bool success,
        string? error,
        ManufacturingContractDto? before,
        ManufacturingContractDto? after)
    {
        var loginUser = HttpContext.Session.GetLoginUser();
        await _auditLogService.WriteAsync(new()
        {
            ActionType = actionType,
            Module = "Contract",
            EntityName = "XMYLB",
            EntityId = entityId.Trim(),
            UserName = loginUser?.UserName ?? string.Empty,
            DisplayName = loginUser?.DisplayName,
            RoleName = loginUser?.RoleName,
            ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            IsSuccess = success,
            ErrorMessage = error,
            BeforeData = before == null ? null : JsonSerializer.Serialize(before),
            AfterData = after == null ? null : JsonSerializer.Serialize(after)
        });
    }

    private static ContractListItemViewModel ToListItem(ManufacturingContractDto dto)
    {
        return new ContractListItemViewModel
        {
            QuotationPlanNo = dto.QuotationPlanNo,
            ContractNo = dto.ContractNo,
            LegacyContractNo = dto.LegacyContractNo,
            ProjectName = dto.ProjectName,
            Owner = dto.Owner,
            SignDate = dto.SignDate,
            DeliveryDate = dto.DeliveryDate,
            TotalAmount = dto.TotalAmount,
            CurrentStatus = dto.CurrentStatus,
            Quoter = dto.Quoter
        };
    }

    private static ContractEditViewModel ToEditModel(ManufacturingContractDto dto)
    {
        return new ContractEditViewModel
        {
            ContractNo = dto.ContractNo,
            ProjectName = dto.ProjectName,
            SignDate = dto.SignDate,
            Owner = dto.Owner,
            ContractContent = dto.ContractContent,
            DeliveryDate = dto.DeliveryDate,
            TotalAmount = dto.TotalAmount,
            CustomerNo = dto.CustomerNo,
            SignCompany = dto.SignCompany,
            CurrentStatus = dto.CurrentStatus,
            Remark = dto.Remark
        };
    }
}

public class ContractListViewModel
{
    public string? Keyword { get; set; }
    public bool IncludeHistory { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;
    public List<ContractListItemViewModel> Items { get; set; } = [];

    /// <summary>当前登录用户名，用于判断是否有权限编辑/删除合同。</summary>
    public string CurrentUserName { get; set; } = string.Empty;
}

public class ContractListItemViewModel
{
    /// <summary>XMYLB.bjd_fabh，列表展示为「方案编号」。</summary>
    public string QuotationPlanNo { get; set; } = string.Empty;
    public string ContractNo { get; set; } = string.Empty;
    public string LegacyContractNo { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTime? SignDate { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public decimal TotalAmount { get; set; }
    public int CurrentStatus { get; set; }

    /// <summary>报价人（来自BJFAT.bjr），用于判断当前用户是否有权限编辑/删除。</summary>
    public string Quoter { get; set; } = string.Empty;
}

public class ContractEditViewModel
{
    [Display(Name = "合同编号")]
    public string ContractNo { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入项目名称")]
    [StringLength(50, ErrorMessage = "项目名称最多 50 个字符")]
    [Display(Name = "项目名称")]
    public string ProjectName { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "签订时间")]
    public DateTime? SignDate { get; set; }

    [StringLength(10, ErrorMessage = "负责人最多 10 个字符")]
    [Display(Name = "负责人")]
    public string Owner { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入合同内容")]
    [StringLength(100, ErrorMessage = "合同内容最多 100 个字符")]
    [Display(Name = "合同内容")]
    public string ContractContent { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "交货时间")]
    public DateTime? DeliveryDate { get; set; }

    [Range(0, 999999999999.9999, ErrorMessage = "合同总金额必须大于等于 0")]
    [Display(Name = "合同总金额")]
    public decimal TotalAmount { get; set; }

    [StringLength(10, ErrorMessage = "客户编号最多 10 个字符")]
    [Display(Name = "客户编号")]
    public string CustomerNo { get; set; } = string.Empty;

    [StringLength(10, ErrorMessage = "签约单位最多 10 个字符")]
    [Display(Name = "签约单位")]
    public string SignCompany { get; set; } = string.Empty;

    [Display(Name = "当前状态")]
    public int CurrentStatus { get; set; }

    [StringLength(200, ErrorMessage = "备注最多 200 个字符")]
    [Display(Name = "备注")]
    public string? Remark { get; set; }
}

public class ContractCreateViewModel
{
    [Required(ErrorMessage = "请输入合同编号")]
    [StringLength(20, ErrorMessage = "合同编号最多 20 个字符")]
    [Display(Name = "合同编号")]
    public string ContractNo { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入项目名称")]
    [StringLength(50, ErrorMessage = "项目名称最多 50 个字符")]
    [Display(Name = "项目名称")]
    public string ProjectName { get; set; } = string.Empty;

    [StringLength(10, ErrorMessage = "旧合同号最多 10 个字符")]
    [Display(Name = "旧合同号")]
    public string? LegacyContractNo { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "签订时间")]
    public DateTime? SignDate { get; set; }

    [StringLength(10, ErrorMessage = "负责人最多 10 个字符")]
    [Display(Name = "负责人")]
    public string Owner { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入合同内容")]
    [StringLength(100, ErrorMessage = "合同内容最多 100 个字符")]
    [Display(Name = "合同内容")]
    public string ContractContent { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "交货时间")]
    public DateTime? DeliveryDate { get; set; }

    [Range(0, 999999999999.9999, ErrorMessage = "合同总金额必须大于等于 0")]
    [Display(Name = "合同总金额")]
    public decimal TotalAmount { get; set; }

    [StringLength(10, ErrorMessage = "客户编号最多 10 个字符")]
    [Display(Name = "客户编号")]
    public string? CustomerNo { get; set; }

    [StringLength(10, ErrorMessage = "签约单位最多 10 个字符")]
    [Display(Name = "签约单位")]
    public string? SignCompany { get; set; }

    [StringLength(20, ErrorMessage = "报价方案号最多 20 个字符")]
    [Display(Name = "报价方案号")]
    public string? QuotationPlanNo { get; set; }

    [Display(Name = "当前状态")]
    public int CurrentStatus { get; set; } = 10;

    [StringLength(200, ErrorMessage = "备注最多 200 个字符")]
    [Display(Name = "备注")]
    public string? Remark { get; set; }
}
