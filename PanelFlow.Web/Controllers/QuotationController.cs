using Microsoft.AspNetCore.Mvc;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Core.Services;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Extensions;
using PanelFlow.Web.Extensions;
using PanelFlow.Web.Filters;
using PanelFlow.Web.Helpers;
using PanelFlow.Web.Models.Quotation;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Globalization;
using System.Text.Json;

namespace PanelFlow.Web.Controllers;

[RoleAuthorize(RoleNames.Admin, RoleNames.Quoter, RoleNames.ProductionManager)]
public class QuotationController : Controller
{
    private readonly IQuotationService _quotationService;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<QuotationController> _logger;
    private readonly IAuditLogService _auditLogService;
    private readonly IElementDictService _elementDictService;
    private readonly IQuotationStructureService _structureService;

    public QuotationController(
        IQuotationService quotationService,
        ApplicationDbContext db,
        ILogger<QuotationController> logger,
        IAuditLogService auditLogService,
        IElementDictService elementDictService,
        IQuotationStructureService structureService)
    {
        _quotationService = quotationService;
        _db = db;
        _logger = logger;
        _auditLogService = auditLogService;
        _elementDictService = elementDictService;
        _structureService = structureService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, bool includeHistory = false, int page = 1, int pageSize = 30)
    {
        ViewData["Title"] = "报价单";
        ViewData["BreadcrumbTitle"] = "报价单";
        ViewData["CurrentUserName"] = HttpContext.Session.GetLoginUser()?.UserName?.Trim() ?? string.Empty;

        var result = await _quotationService.GetListAsync(keyword, includeHistory, page, pageSize);
        return View(new QuotationListViewModel
        {
            Keyword = keyword?.Trim(),
            IncludeHistory = includeHistory,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            TotalPages = result.TotalPages,
            Items = result.Items
        });
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "新建报价单";
        ViewData["BreadcrumbTitle"] = "新建报价单";
        return View(new QuotationEditViewModel
        {
            CurrentStatus = 1
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(QuotationEditViewModel model)
    {
        ModelState.Remove(nameof(QuotationEditViewModel.CreatedAt));

        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "新建报价单";
            ViewData["BreadcrumbTitle"] = "新建报价单";
            return View(model);
        }

        if (string.IsNullOrWhiteSpace(model.CustomerName) || string.IsNullOrWhiteSpace(model.CustomerAlias))
        {
            ModelState.AddModelError(string.Empty, "请先通过关键字搜索并选择客户");
            ViewData["Title"] = "新建报价单";
            ViewData["BreadcrumbTitle"] = "新建报价单";
            return View(model);
        }

        var loginUser = HttpContext.Session.GetLoginUser();
        var (success, message) = await _quotationService.CreateAsync(ToCreateDto(model, loginUser?.UserName));
        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            ViewData["Title"] = "新建报价单";
            ViewData["BreadcrumbTitle"] = "新建报价单";
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> SearchCustomers(string? keyword)
    {
        var kw = (keyword ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(kw))
            return Json(Array.Empty<object>());

        // 仅包含有别名的客户；gsld/gsmc Trim 后与展示一致，再判空与关键字匹配
        var items = await _db.KhylbCustomers
            .AsNoTracking()
            .Where(x =>
                x.gsld.Trim() != string.Empty &&
                (x.gsmc.Trim().Contains(kw) || x.gsld.Trim().Contains(kw)))
            .OrderBy(x => x.gsmc)
            .Take(20)
            .Select(x => new
            {
                CompanyNo = (x.gsbh ?? string.Empty).Trim(),
                CompanyName = (x.gsmc ?? string.Empty).Trim(),
                Alias = (x.gsld ?? string.Empty).Trim()
            })
            .ToListAsync();

        return Json(items);
    }

    /// <summary>
    /// 结构维护页检索：与 Index 行操作条件一致，仅返回本人且未成立(dqzt≠10)的报价单。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchQuotations(string? keyword)
    {
        var loginUser = HttpContext.Session.GetLoginUser();
        var loginUserName = (loginUser?.UserName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(loginUserName))
            return Json(Array.Empty<object>());

        var kw = (keyword ?? string.Empty).Trim();
        var cutoff = DateTime.Today.AddYears(-2);

        var query =
            from q in _db.BjfatQuotations.AsNoTracking().WhereOwnerOperable(loginUserName)
            join c0 in _db.KhylbCustomers.AsNoTracking()
                on q.khbh.Trim() equals c0.gsbh into cg
            from c in cg.DefaultIfEmpty()
            where q.fasj.HasValue && q.fasj.Value >= cutoff
            select new { q, c };

        if (!string.IsNullOrWhiteSpace(kw))
        {
            query = query.Where(x =>
                x.q.fabh.Contains(kw) ||
                x.q.famc.Contains(kw) ||
                x.q.bjr.Contains(kw) ||
                x.q.khbh.Contains(kw) ||
                (x.c != null && x.c.gsmc.Contains(kw)) ||
                (x.c != null && x.c.gsld.Contains(kw)));
        }

        var items = await query
            .OrderByDescending(x => x.q.fasj)
            .ThenByDescending(x => x.q.fabh)
            .Take(20)
            .Select(x => new
            {
                quotationNo = (x.q.fabh ?? string.Empty).Trim(),
                quotationName = (x.q.famc ?? string.Empty).Trim(),
                quoter = (x.q.bjr ?? string.Empty).Trim(),
                customerName = (x.c != null ? (x.c.gsmc ?? string.Empty).Trim() : string.Empty),
                currentStatus = x.q.dqzt
            })
            .ToListAsync();

        return Json(items);
    }

    [HttpGet]
    public IActionResult StructureMaintain()
    {
        ViewData["Title"] = "报价单结构维护";
        ViewData["BreadcrumbTitle"] = "报价单结构维护";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetQuotationTree(string fabh)
    {
        var loginUser = HttpContext.Session.GetLoginUser();
        var loginUserName = (loginUser?.UserName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(loginUserName))
        {
            return Unauthorized(new { success = false, message = "登录已失效，请重新登录后再试" });
        }

        var tree = await _structureService.GetTreeAsync(
            fabh ?? string.Empty, loginUserName, loginUser?.RoleName ?? string.Empty);
        if (tree == null)
        {
            return NotFound(new { success = false, message = "报价单不存在" });
        }

        return Json(new { success = true, data = tree });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyStructure([FromBody] StructureApplyRequest? request)
    {
        if (request == null)
        {
            return BadRequest(new { success = false, message = "请求无效" });
        }

        var loginUser = HttpContext.Session.GetLoginUser();
        var loginUserName = (loginUser?.UserName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(loginUserName))
        {
            return Unauthorized(new { success = false, message = "登录已失效，请重新登录后再试" });
        }

        var result = await _structureService.ApplyAsync(
            request, loginUserName, loginUser?.RoleName ?? string.Empty);

        if (!result.Success)
        {
            return BadRequest(new { success = false, message = result.Message });
        }

        return Ok(new
        {
            success = true,
            message = result.Message,
            addedCount = result.AddedCount,
            skippedCount = result.SkippedCount,
            deletedCount = result.DeletedCount,
            renamedCount = result.RenamedCount,
            reorderedCount = result.ReorderedCount,
            totalRowsWritten = result.TotalRowsWritten
        });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        ViewData["Title"] = "编辑报价单";
        ViewData["BreadcrumbTitle"] = "编辑报价单";

        var loginUser = HttpContext.Session.GetLoginUser();
        var userName = (loginUser?.UserName ?? string.Empty).Trim();

        var dto = await _quotationService.GetByQuotationNoAsync(id);
        if (dto == null)
        {
            TempData["ErrorMessage"] = "报价单不存在";
            return RedirectToAction(nameof(Index));
        }

        var (allowed, accessMessage) = await _quotationService.ValidateEditAccessAsync(id, userName);
        if (!allowed)
        {
            TempData["ErrorMessage"] = accessMessage;
            return RedirectToAction(nameof(Index));
        }

        return View(await ToEditModelAsync(dto, userName));
    }

    /// <summary>
    /// 报价明细入口：导入元件 / 填写报价。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Price(string id, string? section)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo))
        {
            TempData["ErrorMessage"] = "报价单不存在";
            return RedirectToAction(nameof(Index));
        }

        var dto = await _quotationService.GetByQuotationNoAsync(quotationNo);
        if (dto == null)
        {
            TempData["ErrorMessage"] = "报价单不存在";
            return RedirectToAction(nameof(Index));
        }

        var normalizedSection = (section ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedSection != PriceSection.ImportComponents && normalizedSection != PriceSection.FillPrice)
        {
            normalizedSection = dto.CurrentStatus == 1 ? PriceSection.ImportComponents : PriceSection.FillPrice;
        }

        return normalizedSection == PriceSection.FillPrice
            ? RedirectToAction(nameof(FillPrice), new { id = quotationNo })
            : RedirectToAction(nameof(ImportComponents), new { id = quotationNo });
    }

    [HttpGet]
    public async Task<IActionResult> ImportComponents(string id)
    {
        var viewModel = await BuildQuotationPriceViewModelAsync(id, PriceSection.ImportComponents);
        if (viewModel == null)
        {
            TempData["ErrorMessage"] = "报价单不存在";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = "报价";
        ViewData["BreadcrumbTitle"] = "报价";
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> FillPrice(string id)
    {
        var viewModel = await BuildQuotationPriceViewModelAsync(id, PriceSection.FillPrice);
        if (viewModel == null)
        {
            TempData["ErrorMessage"] = "报价单不存在";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = "报价";
        ViewData["BreadcrumbTitle"] = "报价";
        return View(viewModel);
    }

    /// <summary>
    /// 报价单详情：与填写报价相同的目录树与表格展示，只读。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Details(string id)
    {
        var viewModel = await BuildQuotationPriceViewModelAsync(id, PriceSection.FillPrice);
        if (viewModel == null)
        {
            TempData["ErrorMessage"] = "报价单不存在";
            return RedirectToAction(nameof(Index));
        }

        ViewData["Title"] = "报价单详情";
        ViewData["BreadcrumbTitle"] = "报价单详情";
        viewModel.IsReadOnlyView = true;
        viewModel.CanEdit = false;
        return View("FillPrice", viewModel);
    }

    private async Task<QuotationPriceViewModel?> BuildQuotationPriceViewModelAsync(string id, string activeSection)
    {
        ViewData["Title"] = "报价";
        ViewData["BreadcrumbTitle"] = "报价";

        var dto = await _quotationService.GetByQuotationNoAsync(id);
        if (dto == null)
        {
            return null;
        }

        var quotationNo = (dto.QuotationNo ?? string.Empty).Trim();

        // 一次性加载全部 BJB 行（4/8/12 位），在内存中构建树，避免多次往返数据库
        var allRows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Lx = x.x_lx
            })
            .ToListAsync();

        // 过滤特殊行，按编码长度分组
        var rows4 = allRows.Where(x => x.Code.Length == 4 && x.Code != "0" && x.Code != "9999")
                           .OrderBy(x => x.Code).ToList();
        var rows8 = allRows.Where(x => x.Code.Length == 8).ToList();

        // 以Level 2编码前8位作为 HashSet，快速判断 HasLevel3Children
        var level3L2Prefixes = allRows
            .Where(x => x.Code.Length == 12)
            .Select(x => x.Code[..8])
            .ToHashSet(StringComparer.Ordinal);

        // 以Level 1编码作为 HashSet，快速判断 Level 1 节点是否有 Level 2 子行
        var level1WithChildren = rows8
            .Select(x => x.Code[..4])
            .ToHashSet(StringComparer.Ordinal);

        // 构建 Level 1 树节点（含 Level 2 子节点）
        var treeNodes = rows4.Select(l1 =>
        {
            var children = rows8
                .Where(l2 => l2.Code.StartsWith(l1.Code, StringComparison.Ordinal))
                .OrderBy(l2 => l2.Code)
                .Select(l2 => new QuotationTreeLevel2NodeViewModel
                {
                    Code = l2.Code,
                    Name = string.IsNullOrWhiteSpace(l2.Name) ? l2.Code : l2.Name,
                    HasLevel3Children = level3L2Prefixes.Contains(l2.Code)
                })
                .ToList();

            return new QuotationTreeNodeViewModel
            {
                Code = l1.Code,
                Name = string.IsNullOrWhiteSpace(l1.Name) ? l1.Code : l1.Name,
                NodeType = level1WithChildren.Contains(l1.Code) ? "cabinet" : "leaf",
                Level2Children = children
            };
        }).ToList();

        // 构建属性视图树节点：Level 2 非器件行，按 x_lx 分组（x_lx=1 为器件，跳过）
        var attrNodes = rows8
            .Where(x => x.Lx.HasValue && x.Lx.Value != 1)
            .GroupBy(x => x.Lx!.Value)
            .Select(g => new QuotationAttrNodeViewModel
            {
                Xlx = g.Key,
                Name = g.First().Name,
                Count = g.Count()
            })
            .OrderBy(x => x.Xlx)
            .ToList();

        // 权限判断：报价人本人或管理员可编辑
        var loginUser = HttpContext.Session.GetLoginUser();
        var loginUserName = (loginUser?.UserName ?? string.Empty).Trim();
        var loginRole = (loginUser?.RoleName ?? string.Empty).Trim();
        var isAdmin = string.Equals(loginRole, RoleNames.Admin, StringComparison.OrdinalIgnoreCase);
        var owner = (dto.Quoter ?? string.Empty).Trim();
        var canEdit = isAdmin || string.Equals(owner, loginUserName, StringComparison.OrdinalIgnoreCase);

        var viewModel = new QuotationPriceViewModel
        {
            QuotationNo = quotationNo,
            QuotationName = (dto.QuotationName ?? string.Empty).Trim(),
            CurrentStatus = dto.CurrentStatus,
            ActiveSection = activeSection,
            TreeNodes = treeNodes,
            AttrNodes = attrNodes,
            CanEdit = canEdit
        };
        return viewModel;
    }

    // ──────────────────────────────────────────────────────────────────────
    // 一、二级叶节点填价：附加费用项 + 属性视图 + 保存
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 获取指定控制柜（cabinet）的 Level 2 附加费用项，或 Level 1 叶节点（leaf）自身行。
    /// - cabinet：返回该柜下 x_bm.Length==8 且无12位子行的行（数据驱动，非硬编码）
    /// - leaf：返回 x_bm==unitCode 的 Level 1 行自身
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCabinetAdditionalItems(string id, string unitCode)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        var code = (unitCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo) || string.IsNullOrWhiteSpace(code))
            return BadRequest(new { success = false, message = "报价单编号和节点编码不能为空" });

        var rows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Unit = (x.x_dw ?? string.Empty).Trim(),
                Price = x.x_bj_dj,
                Qty = x.x_sl,
                FloatRate = x.x_fdds,
                Vendor = (x.x_sccj ?? string.Empty).Trim()
            })
            .ToListAsync();

        // 构建 Level 3 → Level 2 前缀集合，用于判断 Level 2 节点是否有子行（数据驱动）
        var level3L2Prefixes = rows
            .Where(x => x.Code.Length == 12)
            .Select(x => x.Code[..8])
            .ToHashSet(StringComparer.Ordinal);

        var isLeaf = code.Length == 4
            && !rows.Any(x => x.Code.Length == 8 && x.Code.StartsWith(code, StringComparison.Ordinal));

        IEnumerable<object> items;
        if (isLeaf)
        {
            // Level 1 叶节点：返回自身行
            items = rows
                .Where(x => x.Code == code)
                .Select(x => (object)new
                {
                    x_bm = x.Code,
                    x_mc = x.Name,
                    x_ggxh = x.Spec,
                    x_dw = x.Unit,
                    x_bj_dj = x.Price ?? 0m,
                    x_sl = x.Qty ?? 0m,
                    x_fdds = x.FloatRate ?? 0m,
                    x_sccj = x.Vendor
                });
        }
        else
        {
            // cabinet：返回 Level 2 中无12位子行的附加费用项（数据驱动排除器件）
            items = rows
                .Where(x => x.Code.Length == 8
                    && x.Code.StartsWith(code, StringComparison.Ordinal)
                    && !level3L2Prefixes.Contains(x.Code))
                .OrderBy(x => x.Code)
                .Select(x => (object)new
                {
                    x_bm = x.Code,
                    x_mc = x.Name,
                    x_ggxh = x.Spec,
                    x_dw = x.Unit,
                    x_bj_dj = x.Price ?? 0m,
                    x_sl = x.Qty ?? 0m,
                    x_fdds = x.FloatRate ?? 0m,
                    x_sccj = x.Vendor
                });
        }

        return Ok(new { success = true, rows = items.ToList() });
    }

    /// <summary>
    /// 属性视图：按 x_lx 返回全项目所有控制柜该属性的行，每行含控制柜信息。
    /// xlx=0 时返回所有 Level 1 叶节点（无 Level 2 子行）。
    /// 对于缺少该属性行的控制柜，补充 isPlaceholder=true 的占位记录。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAttributeItems(string id, int xlx)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo))
            return BadRequest(new { success = false, message = "报价单编号不能为空" });

        var rows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Unit = (x.x_dw ?? string.Empty).Trim(),
                Price = x.x_bj_dj,
                Qty = x.x_sl,
                FloatRate = x.x_fdds,
                Vendor = (x.x_sccj ?? string.Empty).Trim(),
                Lx = x.x_lx
            })
            .ToListAsync();

        if (xlx == 0)
        {
            // 特殊：Level 1 叶节点（无任何 8 位子行）
            var level1WithChildren = rows
                .Where(x => x.Code.Length == 8)
                .Select(x => x.Code[..4])
                .ToHashSet(StringComparer.Ordinal);

            var leafItems = rows
                .Where(x => x.Code.Length == 4 && x.Code != "0" && x.Code != "9999"
                    && !level1WithChildren.Contains(x.Code))
                .OrderBy(x => x.Code)
                .Select(x => (object)new
                {
                    cabinetCode = (string?)null,
                    cabinetName = (string?)null,
                    x_bm = x.Code,
                    x_mc = x.Name,
                    x_ggxh = x.Spec,
                    x_dw = x.Unit,
                    x_bj_dj = x.Price ?? 0m,
                    x_sl = x.Qty ?? 0m,
                    x_fdds = x.FloatRate ?? 0m,
                    x_sccj = x.Vendor,
                    isPlaceholder = false
                })
                .ToList();

            return Ok(new { success = true, rows = leafItems });
        }

        // 按 x_lx 查目标属性行，建立"控制柜编码 → 属性行"映射
        var attrByL1 = rows
            .Where(x => x.Code.Length == 8 && x.Lx == xlx)
            .ToDictionary(x => x.Code[..4], x => x, StringComparer.Ordinal);

        // 取全部 cabinet 控制柜（有 Level 2 子行的 Level 1 节点）
        var level2L1Prefixes = rows
            .Where(x => x.Code.Length == 8)
            .Select(x => x.Code[..4])
            .ToHashSet(StringComparer.Ordinal);

        // 控制柜名称 map
        var cabinetNameMap = rows
            .Where(x => x.Code.Length == 4 && level2L1Prefixes.Contains(x.Code))
            .ToDictionary(x => x.Code, x => string.IsNullOrWhiteSpace(x.Name) ? x.Code : x.Name,
                StringComparer.Ordinal);

        var result = level2L1Prefixes
            .OrderBy(c => c)
            .Select(cabCode =>
            {
                var cabName = cabinetNameMap.TryGetValue(cabCode, out var n) ? n : cabCode;
                if (attrByL1.TryGetValue(cabCode, out var attr))
                {
                    return (object)new
                    {
                        cabinetCode = cabCode,
                        cabinetName = cabName,
                        x_bm = attr.Code,
                        x_mc = attr.Name,
                        x_ggxh = attr.Spec,
                        x_dw = attr.Unit,
                        x_bj_dj = attr.Price ?? 0m,
                        x_sl = attr.Qty ?? 0m,
                        x_fdds = attr.FloatRate ?? 0m,
                        x_sccj = attr.Vendor,
                        isPlaceholder = false
                    };
                }
                // 该控制柜缺少此属性行，返回占位记录
                return (object)new
                {
                    cabinetCode = cabCode,
                    cabinetName = cabName,
                    x_bm = (string?)null,
                    x_mc = (string?)null,
                    x_ggxh = (string?)null,
                    x_dw = (string?)null,
                    x_bj_dj = 0m,
                    x_sl = 0m,
                    x_fdds = 0m,
                    x_sccj = (string?)null,
                    isPlaceholder = true
                };
            })
            .ToList();

        return Ok(new { success = true, rows = result });
    }

    /// <summary>
    /// 按精确 x_bm 批量 UPDATE Level 1/2 叶节点行的多个字段（规格/单位/单价/数量/浮动/厂家）。
    /// 不触发 x_bm 重排序；仅允许报价人本人或管理员操作。
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLeafItemFields([FromBody] SaveLeafItemFieldsRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.QuotationNo))
            return BadRequest(new { success = false, message = "请求参数不完整" });
        if (request.Items == null || request.Items.Count == 0)
            return Ok(new { success = true, message = "无需更新" });

        var quotationNo = request.QuotationNo.Trim();

        // 权限校验
        var loginUser = HttpContext.Session.GetLoginUser();
        if (loginUser == null)
            return Unauthorized(new { success = false, message = "请先登录" });

        var dto = await _quotationService.GetByQuotationNoAsync(quotationNo);
        if (dto == null)
            return BadRequest(new { success = false, message = "报价单不存在" });
        if (dto.CurrentStatus == 10)
            return BadRequest(new { success = false, message = "已成立的报价单不允许修改" });

        var loginUserName = (loginUser.UserName ?? string.Empty).Trim();
        var loginRole = (loginUser.RoleName ?? string.Empty).Trim();
        var isAdmin = string.Equals(loginRole, RoleNames.Admin, StringComparison.OrdinalIgnoreCase);
        var owner = (dto.Quoter ?? string.Empty).Trim();
        if (!isAdmin && !string.Equals(owner, loginUserName, StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { success = false, message = "无权修改此报价单" });

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in request.Items)
            {
                var code = (item.Code ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(code)) continue;
                var dj = item.Price * (1 + (item.FloatRate) / 100m);
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE BJB SET
    x_ggxh      = {item.Spec},
    x_dw        = {item.Unit},
    x_bj_dj     = {item.Price},
    x_bjb_dj    = {item.Price},
    x_bjb_bj    = {item.Price},
    x_sl        = {item.Qty},
    x_fdds      = {item.FloatRate},
    x_sccj      = {item.Vendor},
    x_dj        = {dj}
WHERE fabh = {quotationNo}
  AND LTRIM(RTRIM(x_bm)) = {code}");
            }
            await tx.CommitAsync();
            return Ok(new { success = true, message = $"已保存 {request.Items.Count} 行" });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "SaveLeafItemFields 失败。quotationNo={QuotationNo}", quotationNo);
            return StatusCode(500, new { success = false, message = $"保存失败：{ex.Message}" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetCabinetComponents(string id, string unitCode)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        var cabinetCode = (unitCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo) || string.IsNullOrWhiteSpace(cabinetCode))
            return BadRequest(new { success = false, message = "报价单编号和节点编码不能为空" });

        var rows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Unit = (x.x_dw ?? string.Empty).Trim(),
                Price = x.x_bj_dj,
                Qty = x.x_sl,
                FloatRate = x.x_fdds,
                Vendor = (x.x_sccj ?? string.Empty).Trim(),
                Wzdh = (x.x_wzdh ?? string.Empty).Trim()
            })
            .ToListAsync();

        var components = rows
            .Where(x => x.Code.Length == 12
                        && x.Code.StartsWith(cabinetCode, StringComparison.Ordinal)
                        && x.Code.Substring(4, 4) == "0001")
            .OrderBy(x => x.Code)
            .Select((x, index) => new
            {
                seq = index + 1,
                x_bm = x.Code,
                x_mc = x.Name,
                x_ggxh = x.Spec,
                x_dw = x.Unit,
                x_dj = x.Price ?? 0m,
                x_sl = x.Qty ?? 0m,
                x_fdds = x.FloatRate ?? 0m,
                x_sccj = x.Vendor,
                // 如果 DB 中 x_wzdh 为空，实时计算
                x_wzdh = string.IsNullOrWhiteSpace(x.Wzdh) ? NormalizeSpec(x.Spec) : x.Wzdh
            })
            .ToList();

        return Ok(new { success = true, rows = components });
    }

    /// <summary>
    /// 与 GetCabinetComponents 相同行序（按 x_bm）；LEFT JOIN STD_PRICE_BJ，关联 b.x_key_ry = s.ggxh，返回标准价 bj。不使用 TRIM/f_cleanString，便于索引利用。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCabinetReferenceBj(string id, string unitCode)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        var cabinetCode = (unitCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo) || string.IsNullOrWhiteSpace(cabinetCode))
            return BadRequest(new { success = false, message = "报价单编号和节点编码不能为空" });

        var rows = await _db.Database.SqlQueryRaw<CabinetReferenceBjRow>($@"
SELECT s.bj AS RefBj
FROM BJB AS b
LEFT JOIN STD_PRICE_BJ AS s
    ON b.x_key_ry = s.ggxh
WHERE b.fabh = {{0}}
  AND LEN(b.x_bm) = 12
  AND SUBSTRING(b.x_bm, 1, 4) = {{1}}
  AND SUBSTRING(b.x_bm, 5, 4) = '0001'
ORDER BY b.x_bm",
            quotationNo, cabinetCode).ToListAsync();

        return Ok(new
        {
            success = true,
            rows = rows.Select(x => new { refBj = x.RefBj }).ToList()
        });
    }

    /// <summary>
    /// 获取指定控制柜下元件的参考价格（来自 STD_PRICE_HISTORY）。
    /// 返回数组与 GetCabinetComponents 行序一一对应，无匹配记录的位置返回 null。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetReferencePrice(string id, string unitCode)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        var cabinetCode = (unitCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo) || string.IsNullOrWhiteSpace(cabinetCode))
            return BadRequest(new { success = false, message = "报价单编号和节点编码不能为空" });

        var loginUser = HttpContext.Session.GetLoginUser();
        if (loginUser == null)
            return Unauthorized(new { success = false, message = "登录已失效，请重新登录" });

        _logger.LogInformation("[GetReferencePrice] 开始查询参考价格。fabh={Fabh}, unitCode={UnitCode}", quotationNo, cabinetCode);

        // 查询当前控制柜下的元件行，与 GetCabinetComponents 使用相同筛选和排序
        var bjbRows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Wzdh = (x.x_wzdh ?? string.Empty).Trim(),
                Lx = x.x_lx
            })
            .ToListAsync();

        _logger.LogInformation("[GetReferencePrice] 查询到 BJB 行数={Total}, fabh={Fabh}", bjbRows.Count, quotationNo);

        var components = bjbRows
            .Where(x => x.Code.Length == 12
                        && x.Code.StartsWith(cabinetCode, StringComparison.Ordinal)
                        && x.Code.Substring(4, 4) == "0001")
            .OrderBy(x => x.Code)
            .ToList();

        _logger.LogInformation("[GetReferencePrice] 过滤后元件行数={Count} (Length==12 && StartsWith({CabinetCode}) && Substring(4,4)==\"0001\")", components.Count, cabinetCode);

        // 如果过滤条件 Substring(4,4)=="0001" 导致为空，打印前几行的实际 Substring 值
        if (components.Count == 0 && bjbRows.Count > 0)
        {
            var sampleRows = bjbRows
                .Where(x => x.Code.Length == 12 && x.Code.StartsWith(cabinetCode, StringComparison.Ordinal))
                .Take(5)
                .Select(x => new { x.Code, Sub4_4 = x.Code.Length >= 8 ? x.Code.Substring(4, 4) : "N/A" })
                .ToList();
            _logger.LogWarning("[GetReferencePrice] Substring(4,4) 过滤导致结果为空！前5行样本: {@Samples}", sampleRows);
        }

        if (components.Count == 0)
            return Ok(new { success = true, rows = Array.Empty<ReferencePriceRow?>() });

        // 计算每行的 x_wzdh（DB 为空时实时计算）
        var wzdhList = components
            .Select(x => string.IsNullOrWhiteSpace(x.Wzdh) ? NormalizeSpec(x.Spec) : x.Wzdh)
            .ToList();

        // 收集非空的 x_wzdh 用于批量查询
        var distinctWzdh = wzdhList
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Distinct()
            .ToList();

        _logger.LogInformation("[GetReferencePrice] 去重后 x_wzdh 数量={DistinctCount}, 空 wzdh 行数={EmptyCount}",
            distinctWzdh.Count, wzdhList.Count(w => string.IsNullOrWhiteSpace(w)));

        // 打印前5个 wzdh 样本
        if (distinctWzdh.Count > 0)
        {
            var sample = distinctWzdh.Take(5).ToList();
            _logger.LogInformation("[GetReferencePrice] wzdh 样本（前5）: {Samples}", string.Join(" | ", sample));
        }

        // 批量查询 STD_PRICE_HISTORY
        var priceMap = new Dictionary<string, StdPriceHistoryDto>(StringComparer.OrdinalIgnoreCase);
        if (distinctWzdh.Count > 0)
        {
            var priceRecords = await _db.StdPriceHistories
                .AsNoTracking()
                .Where(p => distinctWzdh.Contains(p.x_wzdh))
                .Select(p => new StdPriceHistoryDto
                {
                    Wzdh = p.x_wzdh,
                    LastPrice = p.last_price,
                    AvgPrice = p.avg_price,
                    MinPrice = p.min_price,
                    MaxPrice = p.max_price,
                    AvgCount = p.avg_count
                })
                .ToListAsync();

            _logger.LogInformation("[GetReferencePrice] STD_PRICE_HISTORY 匹配到 {MatchCount}/{DistinctCount} 条记录",
                priceRecords.Count, distinctWzdh.Count);

            // 如果匹配为0但有 wzdh，打印样本帮助排查
            if (priceRecords.Count == 0 && distinctWzdh.Count > 0)
            {
                _logger.LogWarning("[GetReferencePrice] STD_PRICE_HISTORY 匹配为0！检查 wzdh 是否存在于表中。样本 wzdh: {Samples}",
                    string.Join(" | ", distinctWzdh.Take(3)));
            }

            foreach (var rec in priceRecords)
            {
                var key = (rec.Wzdh ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    priceMap[key] = rec;
                }
            }

            // DEBUG: 打印 priceMap 的 key 和 wzdhList 的值，确认是否匹配
            if (priceRecords.Count > 0)
            {
                var mapKeys = priceMap.Keys.Take(3).ToList();
                var listKeys = wzdhList.Where(w => !string.IsNullOrWhiteSpace(w)).Take(3).ToList();
                _logger.LogInformation("[GetReferencePrice] priceMap keys 样本: [{MapKeys}], wzdhList 样本: [{ListKeys}]",
                    string.Join(", ", mapKeys.Select(k => $"'{k}' (len={k.Length})")),
                    string.Join(", ", listKeys.Select(k => $"'{k}' (len={k.Length})")));
            }
        }

        // 按行序构建结果数组，无匹配返回 null
        var matchCount = 0;
        var missCount = 0;
        var rows = wzdhList
            .Select((wzdh, idx) =>
            {
                if (string.IsNullOrWhiteSpace(wzdh))
                    return null;

                if (!priceMap.TryGetValue(wzdh, out var price))
                {
                    missCount++;
                    if (missCount <= 3)
                        _logger.LogWarning("[GetReferencePrice] TryGetValue 未命中: wzdh='{Wzdh}' (len={Len}), priceMap.Count={MapCount}",
                            wzdh, wzdh.Length, priceMap.Count);
                    return null;
                }

                matchCount++;
                return new ReferencePriceRow
                {
                    LastPrice = price.LastPrice,
                    AvgPrice = price.AvgPrice,
                    MinPrice = price.MinPrice,
                    MaxPrice = price.MaxPrice,
                    AvgCount = price.AvgCount
                };
            })
            .ToList();

        _logger.LogInformation("[GetReferencePrice] 最终结果: 总行数={Total}, 匹配={Match}, 未命中={Miss}, null(空wzdh)={Null}",
            rows.Count, matchCount, missCount, rows.Count - matchCount - missCount);

        return Ok(new { success = true, rows });
    }

    [HttpGet]
    public async Task<IActionResult> GetProjectComponentSummary(string id)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo))
            return BadRequest(new { success = false, message = "报价单编号不能为空" });

        var rows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Unit = (x.x_dw ?? string.Empty).Trim(),
                // 与柜体视图 GetCabinetComponents 一致：汇总「单价」列取 x_bj_dj（报价单价），
                // 勿用 x_dj——历史数据常只维护了 x_bj_dj，x_dj 要等 SavePlan 才按浮动率回写。
                Price = x.x_bj_dj ?? 0m,
                Qty = x.x_sl ?? 0m,
                FloatRate = x.x_fdds ?? 0m,
                Vendor = (x.x_sccj ?? string.Empty).Trim()
            })
            .ToListAsync();

        var summary = rows
            .Where(x => x.Code.Length == 12 && x.Code.Substring(4, 4) == "0001")
            .GroupBy(x => new
            {
                x.Name,
                x.Spec,
                x.Unit,
                x.Price,
                x.FloatRate,
                x.Vendor
            })
            .Select((g, index) => new
            {
                seq = index + 1,
                x_mc = g.Key.Name,
                x_ggxh = g.Key.Spec,
                x_dw = g.Key.Unit,
                x_dj = g.Key.Price,
                x_sl = g.Sum(x => x.Qty),
                x_fdds = g.Key.FloatRate,
                x_sccj = g.Key.Vendor,
                // 金额小计 = Σ(x_bj_dj * (1 + x_fdds/100) * x_sl)，与柜体金额列公式一致
                amount = g.Sum(x => x.Price * (1 + x.FloatRate / 100m) * x.Qty),
                matchKey = BuildSummaryMatchKey(g.Select(x => x.Code))
            })
            .OrderBy(x => x.x_mc)
            .ThenBy(x => x.x_ggxh)
            .ThenBy(x => x.x_dw)
            .ThenBy(x => x.x_dj)
            .ThenBy(x => x.x_fdds)
            .ThenBy(x => x.x_sccj)
            .ToList();

        return Ok(new { success = true, rows = summary });
    }

    /// <summary>
    /// 查询某个元件（按标准化指纹 x_wzdh 识别）在本报价单内被哪些控制柜使用。
    /// 口径（Req 17）：使用 x_wzdh 作为元件识别字段，与 STD_PRICE_HISTORY 匹配口径完全统一；
    /// 不参与 价格 / 浮动率 / 厂家 等比较——同一型号在不同柜的价格/厂家差异不影响识别。
    /// 当 x_wzdh 为空时拒绝查询（型号未填则无法识别为"同一元件"）。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetProjectComponentUsage(string id, string wzdh)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo))
            return BadRequest(new { success = false, message = "报价单编号不能为空" });

        var targetWzdh = (wzdh ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(targetWzdh))
        {
            // Req 17.6：元件未填规格型号时，明确返回无法识别，不统计使用情况
            return Ok(new
            {
                success = true,
                rows = Array.Empty<object>(),
                message = "该元件未填规格型号，无法识别使用情况"
            });
        }

        var rows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Wzdh = (x.x_wzdh ?? string.Empty).Trim(),
                Qty = x.x_sl ?? 0m,
                Price = x.x_bj_dj ?? 0m,
                Vendor = (x.x_sccj ?? string.Empty).Trim(),
                Lx = x.x_lx
            })
            .ToListAsync();

        var cabinetNames = rows
            .Where(x => x.Code.Length == 4 && x.Code != "9999")
            .ToDictionary(x => x.Code, x => string.IsNullOrWhiteSpace(x.Name) ? x.Code : x.Name);

        // 对元件行实时计算 wzdh 兜底：DB 中 x_wzdh 字段可能尚未刷新；同时只考察元件行（Lx==11、Code.Length==12、Substring(4,4)=="0001"）
        var components = rows
            .Where(x => x.Lx == 11
                        && x.Code.Length == 12
                        && x.Code.Substring(4, 4) == "0001")
            .Select(x => new
            {
                x.Code,
                x.Name,
                x.Spec,
                x.Qty,
                x.Price,
                x.Vendor,
                EffectiveWzdh = string.IsNullOrWhiteSpace(x.Wzdh) ? NormalizeSpec(x.Spec) : x.Wzdh
            })
            .Where(x => string.Equals(x.EffectiveWzdh, targetWzdh, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var usage = components
            .GroupBy(x => x.Code[..4])
            .Select(g => new
            {
                unitCode = g.Key,
                unitName = cabinetNames.TryGetValue(g.Key, out var unitName) ? unitName : g.Key,
                qty = g.Sum(x => x.Qty),
                // 同一柜中可能有多条同型号但不同价格/厂家的行，便于用户在面板里看出差异
                priceMin = g.Min(x => x.Price),
                priceMax = g.Max(x => x.Price),
                vendors = g.Select(x => x.Vendor)
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList()
            })
            .OrderBy(x => x.unitCode)
            .ToList();

        return Ok(new { success = true, rows = usage });
    }

    /// <summary>
    /// 查询某 x_wzdh 在本报价单内的使用统计（行数、涉及控制柜），供改价前提示用户。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetComponentWzdhSyncStats(string id, string wzdh)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo))
            return BadRequest(new { success = false, message = "报价单编号不能为空" });

        var targetWzdh = (wzdh ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(targetWzdh))
        {
            return Ok(new
            {
                success = true,
                totalRows = 0,
                cabinetCount = 0,
                cabinets = Array.Empty<object>(),
                message = "该元件未填规格型号，无法识别为同一型号"
            });
        }

        var matches = await FindComponentRowsByWzdhAsync(quotationNo, targetWzdh);
        var cabinets = matches
            .GroupBy(x => x.CabCode)
            .Select(g => new
            {
                unitCode = g.Key,
                unitName = g.First().CabName,
                rowCount = g.Count()
            })
            .OrderBy(x => x.unitCode)
            .ToList();

        return Ok(new
        {
            success = true,
            totalRows = matches.Count,
            cabinetCount = cabinets.Count,
            cabinets,
            message = matches.Count == 0
                ? "本项目中未找到相同型号的元件"
                : $"本项目中共有 {matches.Count} 处使用（{cabinets.Count} 个控制柜）"
        });
    }

    /// <summary>
    /// 按 x_wzdh 将单价同步写入本报价单内所有匹配的元件行（x_bj_dj / x_dj 等字段与 SaveProjectComponentSummary 一致）。
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyComponentPriceByWzdh(string id, [FromBody] ApplyComponentPriceByWzdhRequest? request)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo))
            return BadRequest(new { success = false, message = "报价单编号不能为空" });

        if (request == null)
            return BadRequest(new { success = false, message = "请求体不能为空" });

        if (request.NewPrice < 0)
            return BadRequest(new { success = false, message = "单价不能为负数" });

        var explicitCodes = (request.Codes ?? new List<string>())
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<WzdhComponentMatch> matches;
        if (explicitCodes.Count > 0)
        {
            matches = await FindComponentRowsByCodesAsync(quotationNo, explicitCodes);
        }
        else
        {
            var targetWzdh = (request.Wzdh ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(targetWzdh))
                return BadRequest(new { success = false, message = "型号指纹为空，无法同步" });
            matches = await FindComponentRowsByWzdhAsync(quotationNo, targetWzdh);
        }

        if (matches.Count == 0)
        {
            return Ok(new
            {
                success = true,
                updatedRows = 0,
                cabinetCount = 0,
                message = "未找到需要同步的元件行"
            });
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var affected = 0;
            foreach (var row in matches)
            {
                affected += await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE BJB
SET x_bj_dj = {request.NewPrice},
    x_bjb_bj = {request.NewPrice},
    x_bjb_dj = {request.NewPrice},
    x_dj = {request.NewPrice}
WHERE fabh = {quotationNo}
  AND LTRIM(RTRIM(x_bm)) = {row.Code}");
            }

            await tx.CommitAsync();

            var cabinetCount = matches.Select(x => x.CabCode).Distinct(StringComparer.Ordinal).Count();
            var sampleName = matches[0].Name;
            var sampleSpec = matches[0].Spec;
            var label = string.IsNullOrWhiteSpace(sampleName)
                ? (request.Wzdh ?? string.Empty).Trim()
                : $"{sampleName} {sampleSpec}".Trim();

            var message = affected > 0
                ? $"已将「{label}」在本项目的 {affected} 处单价统一更新为 ¥{request.NewPrice:F2}（涉及 {cabinetCount} 个控制柜）。"
                : "未更新任何记录";

            return Ok(new
            {
                success = true,
                updatedRows = affected,
                cabinetCount,
                message
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "同步元件单价失败。quotationNo={QuotationNo}", quotationNo);
            return StatusCode(500, new { success = false, message = $"同步失败：{ex.Message}" });
        }
    }

    /// <summary>
    /// 获取整张报价单的填价进度与异常聚合，供 FillPrice 页右上"进度看板"使用。
    /// 实现对应 spec：.kiro/specs/fill-progress-dashboard/spec.md  Req B-1 / B-2 / B-3。
    ///
    /// 设计要点：
    ///   * 元件行口径与 GetProjectComponentSummary / GetProjectComponentUsage 完全一致
    ///     （x_lx=11 且 x_bm.Length=12 且 Substring(4,4)="0001"），避免不同端点
    ///     "总数对不上"的视觉灾难。
    ///   * 异常 4 类计数互斥规则：negative / zero_price 与 deviation / missing_spec 之间
    ///     允许共存（例如一个 wzdh 空且单价负的行会同时计入 missing_spec 与 negative）。
    ///     这是为了让前端"按类别筛选"时不漏掉任何一类问题。
    ///   * RowSeq：按 x_bm 字典序在柜内枚举从 1 开始，与 Handsontable 表格行号
    ///     （loadCabinetComponents 加载后的展示顺序）保持一致，前端点击"跳转"才能定位。
    ///   * STD_PRICE_HISTORY 用 Dictionary&lt;wzdh,avg_price&gt; 内存查找，
    ///     OrdinalIgnoreCase 比较器与现有 AutoFillPriceFromHistory 一致。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetProjectFillProgress(string id)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo))
            return BadRequest(new { success = false, message = "报价单编号不能为空" });

        // ── 1. 拉取本报价单所有 BJB 行（柜行 + 元件行），用于元件枚举与柜名映射 ──
        var rows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Wzdh = (x.x_wzdh ?? string.Empty).Trim(),
                Price = x.x_bj_dj ?? 0m,
                Lx = x.x_lx
            })
            .ToListAsync();

        // 柜行 → 柜名映射；"9999" 是历史系统约定的非控制柜节点，与既有逻辑保持一致剔除
        var cabinetNames = rows
            .Where(x => x.Code.Length == 4 && x.Code != "9999")
            .GroupBy(x => x.Code)
            .ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().Name) ? g.Key : g.First().Name);

        // 元件行筛选 + 按 x_bm 字典序排序，建立柜内 RowSeq
        var components = rows
            .Where(x => x.Lx == 11
                        && x.Code.Length == 12
                        && x.Code.Substring(4, 4) == "0001")
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ToList();

        if (components.Count == 0)
        {
            return Ok(new
            {
                success = true,
                data = new ProjectFillProgressDto()
            });
        }

        // ── 2. 一次性预取所需 STD_PRICE_HISTORY，避免 N+1 ──
        // 先把 wzdh 兜底好（DB 字段空 → NormalizeSpec(x_ggxh)），再去重，最后查库
        var componentInputs = components
            .Select(c => new FillProgressComponentInput(
                CabinetCode: c.Code[..4],
                CabinetName: cabinetNames.TryGetValue(c.Code[..4], out var n) ? n : c.Code[..4],
                Name: c.Name,
                Spec: c.Spec,
                EffectiveWzdh: string.IsNullOrWhiteSpace(c.Wzdh) ? NormalizeSpec(c.Spec) : c.Wzdh,
                Price: c.Price))
            .ToList();

        var effectiveWzdhs = componentInputs
            .Select(x => x.EffectiveWzdh)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var avgPriceMap = await _db.StdPriceHistories
            .AsNoTracking()
            .Where(h => effectiveWzdhs.Contains(h.x_wzdh) && h.avg_count > 0)
            .Select(h => new { h.x_wzdh, h.avg_price })
            .ToListAsync();

        var avgByWzdh = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in avgPriceMap)
        {
            if (!string.IsNullOrEmpty(h.x_wzdh))
            {
                avgByWzdh[h.x_wzdh] = h.avg_price;
            }
        }

        // ── 3. 调纯函数完成异常归类与计数（同一逻辑被属性测试覆盖） ──
        var dto = FillProgressCalculator.Calculate(componentInputs, avgByWzdh);
        return Ok(new { success = true, data = dto });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProjectComponentSummary(string id, [FromBody] QuotationProjectSummarySaveRequest? request)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo))
            return BadRequest(new { success = false, message = "报价单编号不能为空" });

        if (request == null || request.Items.Count == 0)
            return BadRequest(new { success = false, message = "没有可保存的修改" });

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var affected = 0;
            var matched = 0;
            var submitted = request.Items.Count;
            foreach (var item in request.Items)
            {
                var parsedKey = ParseSummaryMatchKey(item.MatchKey);
                if (parsedKey == null)
                {
                    // Backward compatibility: older frontend payload without matchKey.
                    parsedKey = BuildSummaryMatchKeyFromLegacyItem(item);
                    if (parsedKey == null)
                    {
                        continue;
                    }
                }

                var newUnit = (item.NewUnit ?? string.Empty).Trim();
                var newVendor = (item.NewVendor ?? string.Empty).Trim();

                if (parsedKey.Codes.Count > 0)
                {
                    var affectedByCodes = 0;
                    foreach (var code in parsedKey.Codes)
                    {
                        var trimmedCode = code.Trim();
                        if (string.IsNullOrWhiteSpace(trimmedCode))
                        {
                            continue;
                        }

                        matched += await _db.Database.SqlQueryRaw<int>($@"
SELECT COUNT(1)
FROM BJB
WHERE fabh = {{0}}
  AND LTRIM(RTRIM(x_bm)) = {{1}}", quotationNo, trimmedCode).FirstAsync();

                        affectedByCodes += await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE BJB
SET x_dw = {newUnit},
    x_dj = {item.NewPrice},
    x_fdds = {item.NewFloatRate},
    x_sccj = {newVendor},
    x_bj_dj = {item.NewPrice},
    x_bjb_bj = {item.NewPrice},
    x_bjb_dj = {item.NewPrice}
WHERE fabh = {quotationNo}
  AND LTRIM(RTRIM(x_bm)) = {trimmedCode}");
                    }

                    affected += affectedByCodes;
                    continue;
                }

                var matchedCountSql = await _db.Database.SqlQueryRaw<int>(@"
SELECT COUNT(1)
FROM BJB
WHERE fabh = {0}
  AND LEN(LTRIM(RTRIM(x_bm))) = 12
  AND SUBSTRING(LTRIM(RTRIM(x_bm)), 5, 4) = '0001'
  AND LTRIM(RTRIM(x_mc)) = {1}
  AND LTRIM(RTRIM(x_ggxh)) = {2}
  AND LTRIM(RTRIM(ISNULL(x_dw, ''))) = {3}
  AND CAST(ISNULL(x_dj, 0) AS decimal(18,2)) = CAST({4} AS decimal(18,2))
  AND CAST(ISNULL(x_fdds, 0) AS decimal(18,2)) = CAST({5} AS decimal(18,2))
  AND LTRIM(RTRIM(ISNULL(x_sccj, ''))) = {6}",
                    quotationNo, parsedKey.Name, parsedKey.Spec, parsedKey.Unit, parsedKey.Price, parsedKey.FloatRate, parsedKey.Vendor).ToListAsync();
                matched += matchedCountSql.FirstOrDefault();

                affected += await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE BJB
SET x_dw = {newUnit},
    x_dj = {item.NewPrice},
    x_fdds = {item.NewFloatRate},
    x_sccj = {newVendor},
    x_bj_dj = {item.NewPrice},
    x_bjb_bj = {item.NewPrice},
    x_bjb_dj = {item.NewPrice}
WHERE fabh = {quotationNo}
  AND LEN(LTRIM(RTRIM(x_bm))) = 12
  AND SUBSTRING(LTRIM(RTRIM(x_bm)), 5, 4) = '0001'
  AND LTRIM(RTRIM(x_mc)) = {parsedKey.Name}
  AND LTRIM(RTRIM(x_ggxh)) = {parsedKey.Spec}
  AND LTRIM(RTRIM(ISNULL(x_dw, ''))) = {parsedKey.Unit}
  AND CAST(ISNULL(x_dj, 0) AS decimal(18,2)) = CAST({parsedKey.Price} AS decimal(18,2))
  AND CAST(ISNULL(x_fdds, 0) AS decimal(18,2)) = CAST({parsedKey.FloatRate} AS decimal(18,2))
  AND LTRIM(RTRIM(ISNULL(x_sccj, ''))) = {parsedKey.Vendor}");
            }

            await tx.CommitAsync();
            var message = matched > 0
                ? $"提交 {submitted} 组修改，命中 {matched} 条记录，更新成功 {affected} 条。"
                : $"提交 {submitted} 组修改，命中 0 条记录，请检查旧值匹配。";
            return Ok(new { success = true, message });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "保存项目元件汇总失败。quotationNo={QuotationNo}", quotationNo);
            return StatusCode(500, new { success = false, message = $"保存失败：{ex.Message}" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadImportExcel(string id, IFormFile? file)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo))
            return Json(new { success = false, message = "报价单编号不能为空" });

        if (file == null || file.Length <= 0)
            return Json(new { success = false, message = "请选择 Excel 文件" });

        if (file.Length > 10L * 1024 * 1024)
            return Json(new { success = false, message = "文件大小不能超过 10 MB" });

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            return Json(new { success = false, message = "仅支持 .xls / .xlsx 文件" });
        }

        var dto = await _quotationService.GetByQuotationNoAsync(quotationNo);
        if (dto == null)
            return Json(new { success = false, message = "报价单不存在" });

        try
        {
            await using var stream = file.OpenReadStream();
            using var workbook = WorkbookFactory.Create(stream);
            var sheet = workbook.NumberOfSheets > 0 ? workbook.GetSheetAt(0) : null;
            if (sheet == null)
                return Json(new { success = false, message = "Excel 中没有可读取的工作表" });

            var formatter = new DataFormatter();
            var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();

            var rows = new List<List<string>>();
            var consecutiveEmptyRows = 0;

            // 第 1 行视为标题行，导入时跳过。
            var dataStartRow = sheet.FirstRowNum + 1;
            for (var r = dataStartRow; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                var item = new List<string>(8);
                var hasValue = false;
                for (var c = 0; c < 8; c++)
                {
                    var cell = row?.GetCell(c, MissingCellPolicy.RETURN_BLANK_AS_NULL);
                    var text = cell == null ? string.Empty : formatter.FormatCellValue(cell, evaluator).Trim();
                    item.Add(text);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        hasValue = true;
                    }
                }

                if (!hasValue)
                {
                    consecutiveEmptyRows++;
                    if (consecutiveEmptyRows >= 5)
                    {
                        break;
                    }

                    continue;
                }

                consecutiveEmptyRows = 0;
                rows.Add(item);
                if (rows.Count >= 5000)
                {
                    break;
                }
            }

            return Json(new
            {
                success = true,
                rows,
                rowCount = rows.Count,
                reachedLimit = rows.Count >= 5000
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excel 解析失败 fabh={fabh}", id);
            return Json(new { success = false, message = $"Excel 读取失败：{ex.Message}" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SaveImportExcel([FromBody] QuotationExcelSaveRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.QuotationNo))
            return BadRequest(new { success = false, message = "报价单编号不能为空" });

        var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("元件表");
        var headers = new[] { "序号", "单元号", "名称", "规格", "单价", "数量", "生产厂家", "总价" };

        var headerRow = sheet.CreateRow(0);
        for (var c = 0; c < headers.Length; c++)
        {
            headerRow.CreateCell(c).SetCellValue(headers[c]);
        }

        var rows = request.Rows ?? [];
        for (var r = 0; r < rows.Count; r++)
        {
            var row = sheet.CreateRow(r + 1);
            var cols = rows[r] ?? [];
            for (var c = 0; c < 8; c++)
            {
                var value = c < cols.Count ? (cols[c] ?? string.Empty) : string.Empty;
                row.CreateCell(c).SetCellValue(value);
            }
        }

        for (var c = 0; c < 8; c++)
        {
            sheet.AutoSizeColumn(c);
        }

        using var ms = new MemoryStream();
        workbook.Write(ms, leaveOpen: true);
        ms.Position = 0;

        var quotationNo = request.QuotationNo.Trim();
        var fileName = $"报价元件表_{quotationNo}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
        return File(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePlan([FromBody] QuotationPlanSaveRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Fabh))
            return BadRequest(new { success = false, message = "方案编号不能为空" });

        var fabh = request.Fabh.Trim();
        var loginUser = HttpContext.Session.GetLoginUser();
        var loginUserName = (loginUser?.UserName ?? string.Empty).Trim();
        var loginRole = (loginUser?.RoleName ?? string.Empty).Trim();
        var isAdmin = string.Equals(loginRole, RoleNames.Admin, StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(loginUserName))
            return Unauthorized(new { success = false, message = "登录已失效，请重新登录后再试" });

        var quotation = await _db.BjfatQuotations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.fabh == fabh);
        if (quotation == null)
            return NotFound(new { success = false, message = "方案不存在" });

        var owner = (quotation.bjr ?? string.Empty).Trim();
        var isOwner = string.Equals(owner, loginUserName, StringComparison.OrdinalIgnoreCase);
        if (!isAdmin && !isOwner)
            return StatusCode(403, new { success = false, message = "仅报价人本人或管理员可保存该方案" });

        var tableRows = request.TableJson ?? [];
        if (tableRows.Count == 0)
            return BadRequest(new { success = false, message = "表格为空，无法保存方案" });

        var treeNodeNames = (request.TreeNodeNames ?? [])
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (treeNodeNames.Count == 0)
            return BadRequest(new { success = false, message = "目录树为空，请先执行目录预览后再保存方案" });

        // 显式前置校验：目录树节点数量必须等于表格单元拆分总数（Σ SplitCount）。
        // BuildRowsFromTable 内部仍保留 InvalidOperationException 兜底（第二道防线）。
        var sourceUnits = ParseSourceUnits(tableRows);
        var expected = sourceUnits.Sum(u => u.SplitCount);
        if (expected != treeNodeNames.Count)
        {
            return BadRequest(new
            {
                success = false,
                message = "目录树节点数量与表格单元拆分数量不一致，请重新执行目录预览"
            });
        }

        // 第2级默认节点改由通用项字典驱动（Level=2 且 IsDefaultOnImport=1 且 IsEnabled=1，按 SortOrder）。
        // 字典为空时回退到内置默认 5 类，保证行为不退化。
        var defaultLevel2Nodes = await _elementDictService.GetDefaultImportLevel2Async();
        var rowsToInsert = BuildRowsFromTable(tableRows, treeNodeNames, defaultLevel2Nodes);
        if (rowsToInsert.Count == 0)
            return BadRequest(new { success = false, message = "未解析到可保存的目录/元件数据" });

        // --- 负价格校验（Req 10.1, 10.2）：在任何 DB 操作之前 fail fast ---
        var negativePriceRows = rowsToInsert
            .Where(r => r.Xlx == 11 && r.XbjDj < 0)
            .Select(r => new { name = r.Xmc.Trim(), code = r.Xbm.Trim() })
            .ToList();
        if (negativePriceRows.Count > 0)
        {
            return BadRequest(new
            {
                success = false,
                message = "存在负数单价的元件，无法保存",
                negativePriceItems = negativePriceRows
            });
        }

        var lengthErrors = BjbImportFieldLimits.ValidateImportTable(
            tableRows, treeNodeNames, NormalizeSpec);
        if (lengthErrors.Count > 0)
        {
            return BadRequest(new
            {
                success = false,
                message = lengthErrors[0],
                lengthErrors = lengthErrors.Take(20).ToList()
            });
        }

        // --- 计算 x_dj 和 x_wzdh（Req 10.4, 10.5, 12.6）---
        foreach (var row in rowsToInsert)
        {
            // x_wzdh: NormalizeSpec 处理 x_ggxh（BuildRowsFromTable 已设置，此处确保一致性）
            if (row.Xlx == 11)
            {
                row.Xwzdh = BjbImportFieldLimits.Limit(
                    NormalizeSpec(row.Xggxh), BjbImportFieldLimits.XWzdh);
            }

            // x_dj = x_bj_dj * (1 + x_fdds / 100)，x_fdds 为 NULL 视为 0
            var fdds = row.Xfdds;
            row.Xdj = row.XbjDj * (1 + fdds / 100m);
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"DELETE FROM BJB WHERE fabh = {fabh} AND x_bm NOT IN ('0', '9999')");

            foreach (var row in rowsToInsert)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO BJB
(fabh, x_bm, x_mc, x_dw, x_dj, x_fdds, x_sl, x_bj_fdds, x_bj_dj, x_bjb_bj, x_bjb_dj,
 x_bjb_datetime, x_bjb_fdds, x_wzfy, x_flbh, x_ggxh, x_sccj, x_key_ry, x_jsgsbh, x_bz, x_wzdh, x_lx, x_cgf)
VALUES
({fabh}, {row.Xbm}, {row.Xmc}, {string.Empty}, {row.Xdj}, {row.Xfdds}, {row.Xsl}, {0m}, {row.XbjDj}, {row.XbjbBj}, {row.XbjbDj},
 NULL, {0m}, {0m}, {string.Empty}, {row.Xggxh}, {row.Xsccj}, {string.Empty}, {0m}, {string.Empty}, {row.Xwzdh}, {row.Xlx}, {1})");
            }

            var cabinetCount = rowsToInsert.Count(x => x.Xlx == 1 && x.Xbm.Length == 4);
            if (cabinetCount > 0 && quotation.dqzt == 1)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE BJFAT SET dqzt = {0} WHERE fabh = {fabh} AND dqzt = {1}");
            }
            else if (cabinetCount == 0 && quotation.dqzt == 0)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE BJFAT SET dqzt = {1} WHERE fabh = {fabh} AND dqzt = {0}");
            }

            await tx.CommitAsync();
            return Ok(new { success = true, message = $"保存成功，共写入 {rowsToInsert.Count} 条记录。" });
        }
        catch (InvalidOperationException ex)
        {
            await tx.RollbackAsync();
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "保存方案失败。fabh={Fabh}, user={User}", fabh, loginUserName);
            return StatusCode(500, new { success = false, message = $"保存方案失败：{ex.Message}" });
        }
    }

    /// <summary>
    /// 自动填价：查询历史报价并批量更新当前报价单中单价为0的元件行，返回价格映射和统计信息。
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutoFillPriceFromHistory([FromBody] AutoFillPriceRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Fabh))
            return BadRequest(new { success = false, message = "报价单编号不能为空" });

        var fabh = request.Fabh.Trim();

        // ── 权限校验 ──
        var loginUser = HttpContext.Session.GetLoginUser();
        if (loginUser == null || string.IsNullOrWhiteSpace(loginUser.UserName))
            return Unauthorized(new { success = false, message = "登录已失效，请重新登录后再试" });

        var loginUserName = loginUser.UserName.Trim();
        var loginRole = (loginUser.RoleName ?? string.Empty).Trim();
        var isAdmin = string.Equals(loginRole, RoleNames.Admin, StringComparison.OrdinalIgnoreCase);

        var quotation = await _db.BjfatQuotations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.fabh == fabh);
        if (quotation == null)
            return NotFound(new { success = false, message = "报价单不存在" });

        var owner = (quotation.bjr ?? string.Empty).Trim();
        var isOwner = string.Equals(owner, loginUserName, StringComparison.OrdinalIgnoreCase);
        if (!isAdmin && !isOwner)
            return StatusCode(403, new { success = false, message = "仅报价人本人或管理员可执行自动填价" });

        if (quotation.dqzt == 10)
            return BadRequest(new { success = false, message = "已成立的报价单不允许修改价格" });

        try
        {
            // ── 查询 BJB 中 x_lx=11、x_bm.Trim().Length=12 的元件行 ──
            var allComponents = await _db.BjbItems
                .AsNoTracking()
                .Where(x => x.fabh == fabh && x.x_lx == 11)
                .Select(x => new
                {
                    Code = (x.x_bm ?? string.Empty).Trim(),
                    Spec = (x.x_ggxh ?? string.Empty).Trim(),
                    Wzdh = (x.x_wzdh ?? string.Empty).Trim(),
                    BjbDj = x.x_bjb_dj ?? 0m
                })
                .ToListAsync();

            // 仅保留 x_bm 长度为 12 的元件行
            var elementRows = allComponents
                .Where(x => x.Code.Length == 12)
                .ToList();

            var total = elementRows.Count;

            // 跳过 x_ggxh 为空的行（Req 2.3）
            var validRows = elementRows
                .Where(x => !string.IsNullOrWhiteSpace(x.Spec))
                .ToList();

            // 对 x_wzdh 为空的行实时调用 NormalizeSpec 计算指纹（Req 2.2）
            var rowsWithWzdh = validRows
                .Select(x => new
                {
                    x.Code,
                    x.Spec,
                    x.BjbDj,
                    Wzdh = string.IsNullOrWhiteSpace(x.Wzdh) ? NormalizeSpec(x.Spec) : x.Wzdh
                })
                .Where(x => !string.IsNullOrEmpty(x.Wzdh))
                .ToList();

            if (rowsWithWzdh.Count == 0)
            {
                // 无有效元件行，返回空结果不报错（Req 2.6）
                return Ok(new AutoFillPriceResult
                {
                    Success = true,
                    Matched = 0,
                    Updated = 0,
                    Unmatched = total,
                    Total = total,
                    Message = total == 0
                        ? "当前报价单无元件数据"
                        : $"已匹配 0/{total} 个元件的历史报价，{total} 个元件无历史记录",
                    Prices = new Dictionary<string, PriceInfo>()
                });
            }

            // ── 批量查询 STD_PRICE_HISTORY 获取匹配价格（Req 2.4, 2.5）──
            var wzdhSet = rowsWithWzdh
                .Select(x => x.Wzdh)
                .Distinct()
                .ToList();

            var wzdhParams = string.Join(",", wzdhSet.Select((_, i) => $"{{{i}}}"));
            var sql = $@"
SELECT 
    h.x_wzdh AS Wzdh,
    h.last_price AS Price,
    LTRIM(RTRIM(ISNULL(h.x_dw, ''))) AS Unit,
    LTRIM(RTRIM(ISNULL(h.x_sccj, ''))) AS Vendor
FROM STD_PRICE_HISTORY h
WHERE h.x_wzdh IN ({wzdhParams})";

            var historyRows = await _db.Database.SqlQueryRaw<AutoFillPriceRow>(
                sql, wzdhSet.Cast<object>().ToArray()).ToListAsync();

            // 使用 OrdinalIgnoreCase 比较器：SQL Server 默认排序规则大小写不敏感，
            // DB 中存储的 x_wzdh 可能与输入大小写不一致，避免后续 ContainsKey/TryGetValue 漏匹配。
            var priceMap = new Dictionary<string, AutoFillPriceRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in historyRows)
            {
                var key = (row.Wzdh ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    priceMap[key] = row;
                }
            }

            // ── 统计匹配情况 ──
            var matchedWzdhCodes = rowsWithWzdh
                .Where(x => priceMap.ContainsKey(x.Wzdh))
                .ToList();

            var matched = matchedWzdhCodes.Count;
            // 无指纹的行 + 有指纹但无匹配的行
            var unmatched = total - matched;

            // ── 在事务中批量更新 x_bjb_dj=0 的行（Req 5.1, 5.2, 5.3）──
            var rowsToUpdate = matchedWzdhCodes
                .Where(x => x.BjbDj == 0m)
                .ToList();

            var updated = 0;

            if (rowsToUpdate.Count > 0)
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                try
                {
                    foreach (var row in rowsToUpdate)
                    {
                        var historyPrice = priceMap[row.Wzdh].Price;
                        var affectedRows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE BJB
SET x_bjb_dj = {historyPrice},
    x_bjb_bj = {historyPrice},
    x_bj_dj = {historyPrice}
WHERE fabh = {fabh}
  AND LTRIM(RTRIM(x_bm)) = {row.Code}
  AND x_lx = 11
  AND ISNULL(x_bjb_dj, 0) = 0");
                        updated += affectedRows;
                    }

                    await tx.CommitAsync();
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "自动填价批量更新失败。fabh={Fabh}, 预期更新={ExpectedCount}", fabh, rowsToUpdate.Count);
                    return StatusCode(500, new AutoFillPriceResult
                    {
                        Success = false,
                        Message = "自动填价更新失败，数据已回滚，请稍后重试"
                    });
                }
            }

            // ── 构建价格映射返回前端（Req 5.4）──
            // key 使用前端实际持有的 wzdh（即 currentRowWzdh，来自 GetCabinetComponents），
            // 而非 DB 返回的 wzdh 原始大小写，避免前端 prices[wzdh] 查找失败。
            var skipped = matched - updated;
            var prices = new Dictionary<string, PriceInfo>(StringComparer.Ordinal);
            foreach (var inputWzdh in wzdhSet)
            {
                if (priceMap.TryGetValue(inputWzdh, out var info))
                {
                    prices[inputWzdh] = new PriceInfo
                    {
                        Price = info.Price,
                        Unit = info.Unit,
                        Vendor = info.Vendor
                    };
                }
            }

            var message = $"已匹配 {matched}/{total} 个元件的历史报价，实际更新 {updated} 个" +
                          (skipped > 0 ? $"（{skipped} 个已有价格跳过）" : string.Empty) +
                          (unmatched > 0 ? $"，{unmatched} 个元件无历史记录" : string.Empty);

            return Ok(new AutoFillPriceResult
            {
                Success = true,
                Matched = matched,
                Updated = updated,
                Unmatched = unmatched,
                Total = total,
                Message = message,
                Prices = prices
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "自动填价查询失败。fabh={Fabh}", fabh);
            return StatusCode(500, new AutoFillPriceResult
            {
                Success = false,
                Message = "自动填价失败，请稍后重试"
            });
        }
    }

    // 可见性说明：从 private 提升为 internal 以便 PanelFlow.Web.Tests 通过
    // [InternalsVisibleTo("PanelFlow.Web.Tests")] 直接调用。本调整仅扩大可见范围、
    // 不改变方法行为，与设计文档 design.md "风险与未验证项" 中 "BuildRowsFromTable
    // 当前位于 Controller 内部，PBT 测试需要将其从 internal 升级访问可见性" 一致。
    /// <summary>
    /// 导入时为每个控制柜固定插入的第 2 级默认节点（与历史 PB 行为等价）。
    /// 当未传入字典默认列表时作为回退，保证编码顺序：器件固定首位（其下挂 12 位元件）。
    /// </summary>
    private static readonly IReadOnlyList<(string Name, int Xlx)> FallbackDefaultLevel2Nodes =
    [
        ("器件", 1), ("辅料", 12), ("壳体", 13), ("安装", 14), ("包装", 15)
    ];

    internal static List<BjbWriteRow> BuildRowsFromTable(
        List<List<string?>> tableRows,
        List<string> treeNodeNames,
        IReadOnlyList<(string Name, int Xlx)>? defaultLevel2Nodes = null)
    {
        var level2Nodes = defaultLevel2Nodes is { Count: > 0 }
            ? defaultLevel2Nodes
            : FallbackDefaultLevel2Nodes;
        var sourceUnits = ParseSourceUnits(tableRows);
        if (sourceUnits.Count == 0)
        {
            return [];
        }

        var expectedNodeCount = sourceUnits.Sum(x => x.SplitCount);
        if (expectedNodeCount != treeNodeNames.Count)
        {
            throw new InvalidOperationException("目录树节点数量与表格单元拆分数量不一致，请重新执行目录预览。");
        }

        var result = new List<BjbWriteRow>();
        var unitSeq = 0;
        var treeIndex = 0;
        foreach (var sourceUnit in sourceUnits)
        {
            for (var splitIndex = 0; splitIndex < sourceUnit.SplitCount; splitIndex++)
            {
                unitSeq += 1;
                var currentUnitCode = unitSeq.ToString("D4", CultureInfo.InvariantCulture);
                var unitNodeName = treeNodeNames[treeIndex];
                treeIndex += 1;

                result.Add(new BjbWriteRow
                {
                    Xbm = currentUnitCode,
                    Xmc = BjbImportFieldLimits.Limit(unitNodeName, BjbImportFieldLimits.XMc),
                    Xggxh = string.Empty,
                    Xsccj = string.Empty,
                    Xdj = 0m,
                    Xsl = 1m,
                    XbjDj = 0m,
                    XbjbBj = 0m,
                    XbjbDj = 0m,
                    Xlx = 1
                });

                for (var nodeIdx = 0; nodeIdx < level2Nodes.Count; nodeIdx++)
                {
                    var suffix = (nodeIdx + 1).ToString("D4", CultureInfo.InvariantCulture);
                    result.Add(CreateFixedNode(currentUnitCode, suffix, level2Nodes[nodeIdx].Name, level2Nodes[nodeIdx].Xlx));
                }

                var componentSeq = 0;
                foreach (var component in sourceUnit.Components)
                {
                    componentSeq += 1;
                    var componentCode = $"{currentUnitCode}0001{componentSeq.ToString("D4", CultureInfo.InvariantCulture)}";
                    result.Add(new BjbWriteRow
                    {
                        Xbm = componentCode,
                        Xmc = component.Name,
                        Xggxh = component.Spec,
                        Xsccj = component.Vendor,
                        Xwzdh = BjbImportFieldLimits.Limit(
                            NormalizeSpec(component.Spec), BjbImportFieldLimits.XWzdh),
                        Xdj = component.Price,
                        Xsl = component.Qty,
                        XbjDj = component.Price,
                        XbjbBj = component.Price,
                        XbjbDj = component.Price,
                        Xlx = 11
                    });
                }
            }
        }

        return result;
    }

    // 可见性说明：从 private 提升为 internal，与上方 BuildRowsFromTable 同步，
    // 便于 PanelFlow.Web.Tests 在 PBT 测试中直接复用。
    internal static List<SourceUnitBlock> ParseSourceUnits(List<List<string?>> tableRows)
    {
        var units = new List<SourceUnitBlock>();
        SourceUnitBlock? currentUnit = null;

        foreach (var rawRow in tableRows)
        {
            var row = NormalizeColumns(rawRow);
            var c2UnitMarker = row[1];
            var c3Name = row[2];
            var c4Spec = row[3];
            var c5Price = row[4];
            var c6Qty = row[5];
            var c7Vendor = row[6];

            if (!string.IsNullOrWhiteSpace(c2UnitMarker))
            {
                var splitCount = ParseSplitCount(c6Qty);
                currentUnit = new SourceUnitBlock
                {
                    SplitCount = splitCount
                };
                units.Add(currentUnit);
                continue;
            }

            if (currentUnit == null)
            {
                continue;
            }

            var hasComponentData = !string.IsNullOrWhiteSpace(c3Name)
                                   || !string.IsNullOrWhiteSpace(c4Spec)
                                   || !string.IsNullOrWhiteSpace(c5Price)
                                   || !string.IsNullOrWhiteSpace(c6Qty)
                                   || !string.IsNullOrWhiteSpace(c7Vendor);
            if (!hasComponentData)
            {
                continue;
            }

            currentUnit.Components.Add(new ComponentSourceRow
            {
                Name = BjbImportFieldLimits.Limit(c3Name, BjbImportFieldLimits.XMc),
                Spec = BjbImportFieldLimits.Limit(c4Spec, BjbImportFieldLimits.XGgxh),
                Vendor = BjbImportFieldLimits.Limit(c7Vendor, BjbImportFieldLimits.XSccj),
                Price = ParseDecimal(c5Price),
                Qty = ParseDecimal(c6Qty)
            });
        }

        return units;
    }

    private static int ParseSplitCount(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
        }

        if (value <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Floor(value));
    }

    private static List<string> NormalizeColumns(List<string?>? source)
    {
        var normalized = new List<string>(8);
        for (var i = 0; i < 8; i++)
        {
            var value = source != null && i < source.Count ? source[i] : string.Empty;
            normalized.Add((value ?? string.Empty).Trim());
        }

        return normalized;
    }

    private static decimal ParseDecimal(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return 0m;

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var byInvariant)
            ? byInvariant
            : decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var byCurrent)
                ? byCurrent
                : 0m;
    }

    private static BjbWriteRow CreateFixedNode(string unitCode, string suffix, string name, int type)
    {
        return new BjbWriteRow
        {
            Xbm = $"{unitCode}{suffix}",
            Xmc = name,
            Xggxh = string.Empty,
            Xsccj = string.Empty,
            Xdj = 0m,
            Xsl = 0m,
            XbjDj = 0m,
            XbjbBj = 0m,
            XbjbDj = 0m,
            Xlx = type
        };
    }

    private sealed record WzdhComponentMatch(string Code, string CabCode, string CabName, string Name, string Spec);

    /// <summary>
    /// 在本报价单内按 x_wzdh（含 NormalizeSpec 兜底）查找所有元件行。
    /// 口径与 GetProjectComponentUsage 一致。
    /// </summary>
    private async Task<List<WzdhComponentMatch>> FindComponentRowsByWzdhAsync(string quotationNo, string targetWzdh)
    {
        var rows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Wzdh = (x.x_wzdh ?? string.Empty).Trim(),
                Lx = x.x_lx
            })
            .ToListAsync();

        var cabinetNames = rows
            .Where(x => x.Code.Length == 4 && x.Code != "9999")
            .GroupBy(x => x.Code)
            .ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().Name) ? g.Key : g.First().Name);

        return rows
            .Where(x => x.Lx == 11
                        && x.Code.Length == 12
                        && x.Code.Substring(4, 4) == "0001")
            .Select(x => new
            {
                x.Code,
                x.Name,
                x.Spec,
                EffectiveWzdh = string.IsNullOrWhiteSpace(x.Wzdh) ? NormalizeSpec(x.Spec) : x.Wzdh,
                CabCode = x.Code[..4]
            })
            .Where(x => string.Equals(x.EffectiveWzdh, targetWzdh, StringComparison.OrdinalIgnoreCase))
            .Select(x => new WzdhComponentMatch(
                x.Code,
                x.CabCode,
                cabinetNames.TryGetValue(x.CabCode, out var cabName) ? cabName : x.CabCode,
                x.Name,
                x.Spec))
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<List<WzdhComponentMatch>> FindComponentRowsByCodesAsync(string quotationNo, IReadOnlyList<string> codes)
    {
        if (codes.Count == 0)
            return new List<WzdhComponentMatch>();

        var codeSet = new HashSet<string>(codes, StringComparer.Ordinal);
        var rows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Lx = x.x_lx
            })
            .ToListAsync();

        var cabinetNames = rows
            .Where(x => x.Code.Length == 4 && x.Code != "9999")
            .GroupBy(x => x.Code)
            .ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().Name) ? g.Key : g.First().Name);

        return rows
            .Where(x => x.Lx == 11
                        && x.Code.Length == 12
                        && x.Code.Substring(4, 4) == "0001"
                        && codeSet.Contains(x.Code))
            .Select(x => new WzdhComponentMatch(
                x.Code,
                x.Code[..4],
                cabinetNames.TryGetValue(x.Code[..4], out var cabName) ? cabName : x.Code[..4],
                x.Name,
                x.Spec))
            .OrderBy(x => x.Code, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// C# 版 F_CleanString：转小写 → 删除不可见字符 → 全角转半角 → 去掉括号内容 → 只保留字母/数字/中文/单位符号。
    /// 用于生成型号规格的标准化指纹字符串，便于与历史报价比对。
    /// </summary>
    internal static string NormalizeSpec(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var s = input.ToLowerInvariant()
            .Replace("\r", "").Replace("\n", "").Replace("\t", "")
            .Replace("\u00A0", "")   // 不间断空格
            .Replace("\u200B", "");  // 零宽空格

        var sb = new System.Text.StringBuilder(s.Length);
        var parenDepth = 0;

        foreach (var ch in s)
        {
            // 全角转半角
            var c = ch == '\u3000' ? ' '
                  : ch >= '\uFF01' && ch <= '\uFF5E' ? (char)(ch - 65248)
                  : ch;
            c = char.ToLowerInvariant(c);

            if (c == '(') { parenDepth++; continue; }
            if (c == ')') { if (parenDepth > 0) parenDepth--; continue; }
            if (parenDepth > 0) continue;

            // 只保留字母、数字、中文、常见单位符号
            if (char.IsAsciiLetterOrDigit(c) || (c >= '\u4E00' && c <= '\u9FFF'))
                sb.Append(c);
            else if ("μωΩ°±℃φ".IndexOf(c) >= 0)
                sb.Append(c);
            // 其余符号（空格、标点等）全部丢弃
        }

        return sb.ToString();
    }

    private static string BuildSummaryMatchKey(IEnumerable<string> codes)
    {
        return $"codes:{string.Join(",", codes.Select(x => NormalizeKeyPart(x)).Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x))}";
    }

    private static SummaryMatchKey? ParseSummaryMatchKey(string? matchKey)
    {
        var raw = (matchKey ?? string.Empty).Trim();
        if (raw.StartsWith("codes:", StringComparison.OrdinalIgnoreCase))
        {
            var codes = raw["codes:".Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeKeyPart)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return new SummaryMatchKey
            {
                Codes = codes
            };
        }

        var parts = raw.Split("||");
        if (parts.Length != 6)
        {
            return null;
        }

        if (!decimal.TryParse(parts[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
        {
            return null;
        }

        if (!decimal.TryParse(parts[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var floatRate))
        {
            return null;
        }

        return new SummaryMatchKey
        {
            Name = parts[0],
            Spec = parts[1],
            Unit = parts[2],
            Price = price,
            FloatRate = floatRate,
            Vendor = parts[5]
        };
    }

    private static SummaryMatchKey? BuildSummaryMatchKeyFromLegacyItem(QuotationProjectSummaryUpdateItem item)
    {
        var name = NormalizeKeyPart(item.Name);
        var spec = NormalizeKeyPart(item.Spec);
        var unit = NormalizeKeyPart(item.OldUnit);
        var vendor = NormalizeKeyPart(item.OldVendor);

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(spec))
        {
            return null;
        }

        return new SummaryMatchKey
        {
            Name = name,
            Spec = spec,
            Unit = unit,
            Price = item.OldPrice,
            FloatRate = item.OldFloatRate,
            Vendor = vendor
        };
    }

    private static string NormalizeKeyPart(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

﻿    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(QuotationEditViewModel model)
    {
        ModelState.Remove(nameof(QuotationEditViewModel.CreatedAt));
        var loginUser = HttpContext.Session.GetLoginUser();
        var userName = (loginUser?.UserName ?? string.Empty).Trim();

        async Task<IActionResult> ReturnEditViewAsync()
        {
            ViewData["Title"] = "编辑报价单";
            ViewData["BreadcrumbTitle"] = "编辑报价单";
            await PopulateRenameFabhFlagsAsync(model, userName);
            return View(model);
        }

        if (!ModelState.IsValid)
            return await ReturnEditViewAsync();

        if (string.IsNullOrWhiteSpace(model.CustomerName) || string.IsNullOrWhiteSpace(model.CustomerAlias))
        {
            ModelState.AddModelError(string.Empty, "请先通过关键字搜索并选择客户");
            return await ReturnEditViewAsync();
        }

        var before = await _quotationService.GetByQuotationNoAsync(model.QuotationNo);
        if (before == null)
        {
            TempData["ErrorMessage"] = "报价单不存在";
            return RedirectToAction(nameof(Index));
        }

        var (success, message) = await _quotationService.UpdateAsync(ToEditDto(model, before), userName);
        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            return await ReturnEditViewAsync();
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameQuotationNo(string id, string newQuotationNo)
    {
        var loginUser = HttpContext.Session.GetLoginUser();
        var userName = (loginUser?.UserName ?? string.Empty).Trim();
        var oldFabh = (id ?? string.Empty).Trim();
        var newFabh = (newQuotationNo ?? string.Empty).Trim();
        var before = await _quotationService.GetByQuotationNoAsync(oldFabh);

        var result = await _quotationService.RenameFabhAsync(oldFabh, newFabh, userName);

        await WriteQuotationAuditAsync(
            "RenameFabh",
            oldFabh,
            result.Success,
            result.Success ? null : result.Message,
            before,
            result.NewFabh,
            result.AffectedRows,
            newFabh);

        if (!result.Success)
        {
            TempData["ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(Edit), new { id = oldFabh });
        }

        TempData["SuccessMessage"] = $"{result.Message}：{oldFabh} → {result.NewFabh}。请更新书签或收藏链接。";
        return RedirectToAction(nameof(Edit), new { id = result.NewFabh });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var loginUser = HttpContext.Session.GetLoginUser();
        var (success, message) = await _quotationService.DeleteAsync(id, loginUser?.UserName ?? string.Empty);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = success ? $"报价单 {id.Trim()} 已删除" : message;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult MergeExcel()
    {
        ViewData["Title"] = "Excel合并";
        ViewData["BreadcrumbTitle"] = "Excel合并";
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MergeExcelFile(IFormFile? file, [FromForm] int startSeqNo = 0)
    {
        if (file == null || file.Length <= 0)
            return Json(new { success = false, message = "请选择 Excel 文件" });

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            return Json(new { success = false, message = "仅支持 .xls / .xlsx 文件" });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            using var workbook = WorkbookFactory.Create(stream);
            var sheet = workbook.NumberOfSheets > 0 ? workbook.GetSheetAt(0) : null;
            if (sheet == null)
                return Json(new { success = false, message = "Excel 中没有可读取的工作表" });

            // 使用 Excel 文件名（去除扩展名）作为单元号
            var unitName = Path.GetFileNameWithoutExtension(file.FileName) ?? "未知";
            var formatter = new DataFormatter();
            var evaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();

            var headerRow = sheet.GetRow(sheet.FirstRowNum);
            if (headerRow == null)
                return Json(new { success = false, message = "Excel 标题行无效" });

            var colNameIndex = -1;
            var colSpecIndex = -1;
            var colQtyIndex = -1;
            var colVendorIndex = -1;
            var colRemarkIndex = -1;

            for (var c = 0; c < headerRow.LastCellNum; c++)
            {
                var headerCell = headerRow.GetCell(c, MissingCellPolicy.RETURN_BLANK_AS_NULL);
                if (headerCell == null) continue;
                var headerText = formatter.FormatCellValue(headerCell, evaluator).Trim();
                switch (headerText)
                {
                    case "名称":
                        colNameIndex = c;
                        break;
                    case "型号规格":
                        colSpecIndex = c;
                        break;
                    case "数量":
                        colQtyIndex = c;
                        break;
                    case "厂商":
                    case "生产厂家":
                        colVendorIndex = c;
                        break;
                    case "备注":
                        colRemarkIndex = c;
                        break;
                }
            }

            // 必须包含全部 5 个字段，否则忽略该文件
            if (colNameIndex < 0 || colSpecIndex < 0 || colQtyIndex < 0 || colVendorIndex < 0 || colRemarkIndex < 0)
            {
                return Json(new
                {
                    success = false,
                    ignored = true,
                    message = "Excel 缺少必需列（名称/型号规格/数量/厂商/备注），已忽略"
                });
            }

            var rows = new List<List<string>>();
            var seqNo = startSeqNo;
            var dataStartRow = sheet.FirstRowNum + 1;

            seqNo++;
            rows.Add(new List<string>
            {
                seqNo.ToString(),
                unitName,
                string.Empty,
                string.Empty,
                "0.0",
                "1",
                string.Empty,
                "0.0"
            });

            for (var r = dataStartRow; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;

                var name = colNameIndex >= 0
                    ? formatter.FormatCellValue(row.GetCell(colNameIndex), evaluator).Trim()
                    : string.Empty;
                var spec = colSpecIndex >= 0
                    ? formatter.FormatCellValue(row.GetCell(colSpecIndex), evaluator).Trim()
                    : string.Empty;
                var qty = colQtyIndex >= 0
                    ? formatter.FormatCellValue(row.GetCell(colQtyIndex), evaluator).Trim()
                    : string.Empty;
                var vendor = colVendorIndex >= 0
                    ? formatter.FormatCellValue(row.GetCell(colVendorIndex), evaluator).Trim()
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(spec) && string.IsNullOrWhiteSpace(qty))
                    continue;

                seqNo++;
                var qtyValue = decimal.TryParse(qty, NumberStyles.Number, CultureInfo.InvariantCulture, out var q)
                    ? q
                    : (decimal.TryParse(qty, NumberStyles.Number, CultureInfo.CurrentCulture, out var q2) ? q2 : 1m);

                rows.Add(new List<string>
                {
                    seqNo.ToString(),
                    string.Empty,
                    name,
                    spec,
                    "0.0",
                    qtyValue.ToString(CultureInfo.InvariantCulture),
                    vendor,
                    "0.0"
                });

                if (rows.Count >= 5000)
                    break;
            }

            return Json(new
            {
                success = true,
                rows,
                rowCount = rows.Count,
                lastSeqNo = seqNo,
                reachedLimit = rows.Count >= 5000
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取合并Excel失败。FileName={FileName}", file.FileName);
            return Json(new { success = false, message = $"Excel 读取失败：{ex.Message}" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetSheetCount(IFormFile? file)
    {
        if (file == null || file.Length <= 0)
            return Json(new { success = false, message = "文件无法读取" });

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            return Json(new { success = false, message = "仅支持 .xls / .xlsx 文件" });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            using var workbook = WorkbookFactory.Create(stream);

            var sheetCount = workbook.NumberOfSheets;
            var sheetNames = new List<string>(sheetCount);
            for (var i = 0; i < sheetCount; i++)
            {
                sheetNames.Add(workbook.GetSheetName(i));
            }

            await Task.CompletedTask;

            return Json(new { success = true, sheetCount, sheetNames });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取Sheet信息失败。FileName={FileName}", file.FileName);
            return Json(new { success = false, message = "文件无法读取" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MergeExcelMultiSheet(IFormFile? file, [FromForm] int startSeqNo = 0)
    {
        if (file == null || file.Length <= 0)
            return Json(new { success = false, message = "文件无法读取或不包含任何 Sheet" });

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            return Json(new { success = false, message = "仅支持 .xls / .xlsx 文件" });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            using var workbook = WorkbookFactory.Create(stream);

            var totalSheets = workbook.NumberOfSheets;
            if (totalSheets == 0)
                return Json(new { success = false, message = "文件无法读取或不包含任何 Sheet" });

            var rows = new List<List<string>>();
            var seqNo = startSeqNo;
            var importedSheets = 0;
            var ignoredSheets = 0;
            var ignoredSheetNames = new List<string>();
            var ignoredSheetReasons = new List<string>();
            var reachedLimit = false;
            var usedUnitCodes = new HashSet<string>(StringComparer.Ordinal);
            var allSheetNames = new List<string>();

            // 收集所有 Sheet 原始名称（用于单元号冲突检测）
            for (var s = 0; s < totalSheets; s++)
                allSheetNames.Add(workbook.GetSheetName(s));

            // 按 Sheet 索引升序遍历
            for (var i = 0; i < totalSheets; i++)
            {
                var sheet = workbook.GetSheetAt(i);
                if (sheet == null)
                {
                    ignoredSheets++;
                    ignoredSheetNames.Add(workbook.GetSheetName(i));
                    ignoredSheetReasons.Add("Sheet 为空");
                    continue;
                }

                // 2.2 - 表头列映射与 Sheet 有效性检测
                var headerRow = sheet.GetRow(0);
                if (headerRow == null)
                {
                    ignoredSheets++;
                    ignoredSheetNames.Add(sheet.SheetName);
                    ignoredSheetReasons.Add("无表头行");
                    continue;
                }

                int colName = -1, colSpec = -1, colQty = -1, colVendor = -1, colRemark = -1;
                for (var c = headerRow.FirstCellNum; c < headerRow.LastCellNum; c++)
                {
                    var cell = headerRow.GetCell(c);
                    if (cell == null) continue;
                    var cellValue = cell.ToString()?.Trim() ?? string.Empty;

                    if (cellValue == "名称") colName = c;
                    else if (cellValue == "型号规格" || cellValue == "规格型号" || cellValue == "规格" || cellValue == "型号") colSpec = c;
                    else if (cellValue == "数量") colQty = c;
                    else if (cellValue == "厂商" || cellValue == "生产厂家") colVendor = c;
                    else if (cellValue == "备注") colRemark = c;
                }

                if (colName < 0 || colSpec < 0 || colQty < 0 || colVendor < 0 || colRemark < 0)
                {
                    ignoredSheets++;
                    ignoredSheetNames.Add(sheet.SheetName);
                    var missing = new List<string>();
                    if (colName < 0) missing.Add("名称");
                    if (colSpec < 0) missing.Add("型号规格");
                    if (colQty < 0) missing.Add("数量");
                    if (colVendor < 0) missing.Add("厂商");
                    if (colRemark < 0) missing.Add("备注");
                    ignoredSheetReasons.Add($"缺少列：{string.Join("、", missing)}");
                    continue;
                }
                // 2.3 - 单元号唯一性生成
                var sheetName = sheet.SheetName;
                string unitCode;
                if (!usedUnitCodes.Contains(sheetName))
                {
                    unitCode = sheetName;
                }
                else
                {
                    var n = 2;
                    while (true)
                    {
                        var candidate = sheetName + "_" + n;
                        if (!usedUnitCodes.Contains(candidate)
                            && !allSheetNames.Any(name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)))
                        {
                            unitCode = candidate;
                            break;
                        }
                        n++;
                    }
                }
                usedUnitCodes.Add(unitCode);

                // 2.4 - 数据行解析与合并行生成

                // 插入 UnitHeaderRow
                seqNo++;
                rows.Add(new List<string> { seqNo.ToString(), unitCode, "", "", "", "1", "", "" });

                if (rows.Count >= 5000)
                {
                    reachedLimit = true;
                    break;
                }

                // 从第 2 行开始逐行读取数据
                var consecutiveEmptyRows = 0;
                var sheetFullyProcessed = true;

                for (var rowIdx = 1; rowIdx <= sheet.LastRowNum; rowIdx++)
                {
                    var dataRow = sheet.GetRow(rowIdx);

                    var nameValue = dataRow?.GetCell(colName)?.ToString()?.Trim() ?? "";
                    var specValue = dataRow?.GetCell(colSpec)?.ToString()?.Trim() ?? "";
                    var qtyValue = dataRow?.GetCell(colQty)?.ToString()?.Trim() ?? "";

                    // 判断三列是否同时为空
                    if (string.IsNullOrEmpty(nameValue) && string.IsNullOrEmpty(specValue) && string.IsNullOrEmpty(qtyValue))
                    {
                        consecutiveEmptyRows++;
                        if (consecutiveEmptyRows >= 5)
                            break; // 连续 5 行空行，停止当前 Sheet
                        continue;
                    }

                    // 有效行：重置空行计数
                    consecutiveEmptyRows = 0;

                    var vendorValue = dataRow?.GetCell(colVendor)?.ToString()?.Trim() ?? "";

                    seqNo++;
                    rows.Add(new List<string> { seqNo.ToString(), "", nameValue, specValue, "0.0", qtyValue, vendorValue, "0.0" });

                    if (rows.Count >= 5000)
                    {
                        reachedLimit = true;
                        sheetFullyProcessed = false;
                        break;
                    }
                }

                if (reachedLimit)
                {
                    if (!sheetFullyProcessed)
                    {
                        // 当前 Sheet 未完整处理，不计入 importedSheets
                    }
                    break;
                }

                importedSheets++;
            }

            await Task.CompletedTask;

            return Json(new
            {
                success = true,
                rows,
                rowCount = rows.Count,
                lastSeqNo = seqNo,
                reachedLimit,
                totalSheets,
                importedSheets,
                ignoredSheets,
                ignoredSheetNames,
                ignoredSheetReasons
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "多Sheet合并解析失败。FileName={FileName}", file.FileName);
            return Json(new { success = false, message = "Excel 解析失败，请检查文件格式" });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportMergedExcel([FromBody] MergedExcelExportRequest? request)
    {
        if (request == null || request.Rows.Count == 0)
            return BadRequest(new { success = false, message = "没有可导出的数据" });

        var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("合并元件表");
        var headers = new[] { "序号", "单元号", "名称", "规格", "单价", "数量", "生产厂家", "总价" };

        var headerRow = sheet.CreateRow(0);
        for (var c = 0; c < headers.Length; c++)
        {
            headerRow.CreateCell(c).SetCellValue(headers[c]);
        }

        var rows = request.Rows;
        for (var r = 0; r < rows.Count; r++)
        {
            var row = sheet.CreateRow(r + 1);
            var cols = rows[r] ?? [];
            for (var c = 0; c < 8; c++)
            {
                var value = c < cols.Count ? (cols[c] ?? string.Empty) : string.Empty;
                row.CreateCell(c).SetCellValue(value);
            }
        }

        for (var c = 0; c < 8; c++)
        {
            sheet.AutoSizeColumn(c);
        }

        using var ms = new MemoryStream();
        workbook.Write(ms, leaveOpen: true);
        ms.Position = 0;

        var fileName = $"合并元件表_{DateTime.Now:yyyyMMddHHmmss}.xlsx";

        // 统计单元号数量（用于审计记录）
        var unitCount = rows.Count(row => row != null && row.Count > 1 && !string.IsNullOrWhiteSpace(row[1]));

        // 写入审计日志
        var loginUser = HttpContext.Session.GetLoginUser();
        await _auditLogService.WriteAsync(new()
        {
            ActionType = "ExportMergedExcel",
            Module = "Quotation",
            EntityName = "MergedExcel",
            EntityId = fileName,
            UserName = loginUser?.UserName ?? string.Empty,
            DisplayName = loginUser?.DisplayName ?? string.Empty,
            RoleName = loginUser?.RoleName ?? string.Empty,
            ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            IsSuccess = true,
            AfterData = JsonSerializer.Serialize(new
            {
                fileName,
                rowCount = rows.Count,
                unitCount
            })
        });

        return File(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static QuotationDto ToDto(QuotationEditViewModel model)
    {
        return new QuotationDto
        {
            QuotationNo = model.QuotationNo,
            CreatedAt = model.CreatedAt,
            QuotationName = model.QuotationName ?? string.Empty,
            PlanModelNo = model.PlanModelNo,
            Quoter = model.Quoter ?? string.Empty,
            Remark = model.Remark ?? string.Empty,
            CustomerNo = model.CustomerNo ?? string.Empty,
            PlanType = model.PlanType,
            CurrentStatus = model.CurrentStatus
        };
    }

    private static QuotationDto ToEditDto(QuotationEditViewModel model, QuotationDto before)
    {
        return new QuotationDto
        {
            QuotationNo = model.QuotationNo,
            CreatedAt = before.CreatedAt,
            QuotationName = model.QuotationName ?? string.Empty,
            PlanModelNo = before.PlanModelNo,
            Quoter = before.Quoter,
            Remark = model.Remark ?? string.Empty,
            CustomerNo = model.CustomerNo ?? string.Empty,
            PlanType = before.PlanType,
            CurrentStatus = before.CurrentStatus
        };
    }

    private static QuotationDto ToCreateDto(QuotationEditViewModel model, string? loginUserName)
    {
        return new QuotationDto
        {
            QuotationNo = model.QuotationNo,
            CreatedAt = DateTime.Now,
            QuotationName = model.QuotationName ?? string.Empty,
            PlanModelNo = 0,
            Quoter = (loginUserName ?? string.Empty).Trim(),
            Remark = model.Remark ?? string.Empty,
            CustomerNo = model.CustomerNo ?? string.Empty,
            PlanType = 1,
            CurrentStatus = 1
        };
    }

    private async Task<QuotationEditViewModel> ToEditModelAsync(QuotationDto dto, string userName)
    {
        var model = new QuotationEditViewModel
        {
            QuotationNo = dto.QuotationNo,
            CreatedAt = dto.CreatedAt,
            QuotationName = dto.QuotationName,
            PlanModelNo = dto.PlanModelNo,
            Quoter = dto.Quoter,
            Remark = dto.Remark,
            CustomerNo = dto.CustomerNo,
            PlanType = dto.PlanType,
            CurrentStatus = dto.CurrentStatus
        };

        if (!string.IsNullOrWhiteSpace(dto.CustomerNo))
        {
            var customerNo = dto.CustomerNo.Trim();
            var customer = await _db.KhylbCustomers
                .AsNoTracking()
                .Where(x => x.gsbh == customerNo)
                .Select(x => new
                {
                    CompanyName = (x.gsmc ?? string.Empty).Trim(),
                    Alias = (x.gsld ?? string.Empty).Trim()
                })
                .FirstOrDefaultAsync();

            if (customer != null)
            {
                model.CustomerName = customer.CompanyName;
                model.CustomerAlias = customer.Alias;
            }
        }

        await PopulateRenameFabhFlagsAsync(model, userName);
        return model;
    }

﻿    private async Task PopulateRenameFabhFlagsAsync(QuotationEditViewModel model, string userName)
    {
        var (canRename, message) = await _quotationService.CanRenameFabhAsync(model.QuotationNo, userName);
        model.CanRenameFabh = canRename;
        model.RenameFabhBlockedReason = canRename ? null : message;
    }

    private async Task WriteQuotationAuditAsync(
        string actionType,
        string entityId,
        bool success,
        string? errorMessage,
        QuotationDto? before,
        string? newFabh = null,
        IReadOnlyDictionary<string, int>? affectedRows = null,
        string? attemptedNewFabh = null)
    {
        var loginUser = HttpContext.Session.GetLoginUser();
        object? beforePayload = before == null
            ? null
            : new
            {
                fabh = before.QuotationNo,
                quotationName = before.QuotationName,
                dqzt = before.CurrentStatus,
                fasj = before.CreatedAt,
                attemptNewFabh = attemptedNewFabh
            };
        object? afterPayload = success && !string.IsNullOrWhiteSpace(newFabh)
            ? new { fabh = newFabh, affectedRows }
            : null;

        await _auditLogService.WriteAsync(new AuditLogEntry
        {
            ActionType = actionType,
            Module = "Quotation",
            EntityName = "BJFAT",
            EntityId = entityId.Trim(),
            UserName = loginUser?.UserName ?? string.Empty,
            DisplayName = loginUser?.DisplayName,
            RoleName = loginUser?.RoleName,
            ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            IsSuccess = success,
            ErrorMessage = errorMessage,
            BeforeData = beforePayload == null ? null : JsonSerializer.Serialize(beforePayload),
            AfterData = afterPayload == null ? null : JsonSerializer.Serialize(afterPayload)
        });
    }

}

public class QuotationListViewModel
{
    public string? Keyword { get; set; }
    public bool IncludeHistory { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;
    public List<QuotationDto> Items { get; set; } = [];

    public static string GetStatusText(int status)
    {
        return status switch
        {
            0 => "草稿",
            1 => "(无内容)",
            10 => "已成立",
            _ => status.ToString()
        };
    }
}

public class QuotationEditViewModel
{
    [Required(ErrorMessage = "请输入报价单编号")]
    [StringLength(20, ErrorMessage = "报价单编号最多 20 个字符")]
    [Display(Name = "报价单编号")]
    public string QuotationNo { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "创建时间")]
    public DateTime? CreatedAt { get; set; }

    [Required(ErrorMessage = "请输入报价单名称")]
    [StringLength(50, ErrorMessage = "报价单名称最多 50 个字符")]
    [Display(Name = "报价单名称")]
    public string? QuotationName { get; set; }

    [Display(Name = "方案模型编号")]
    public decimal? PlanModelNo { get; set; }

    [StringLength(10, ErrorMessage = "报价人最多 10 个字符")]
    [Display(Name = "报价人")]
    public string? Quoter { get; set; }

    [StringLength(50, ErrorMessage = "备注最多 50 个字符")]
    [Display(Name = "备注")]
    public string? Remark { get; set; }

    [Required(ErrorMessage = "请选择客户")]
    [StringLength(10, ErrorMessage = "客户编号最多 10 个字符")]
    [Display(Name = "客户编号")]
    public string? CustomerNo { get; set; }

    [StringLength(50, ErrorMessage = "客户名称最多 50 个字符")]
    [Display(Name = "客户名称")]
    public string? CustomerName { get; set; }

    [StringLength(10, ErrorMessage = "别名最多 10 个字符")]
    [Display(Name = "别名")]
    public string? CustomerAlias { get; set; }

    [Display(Name = "方案类型")]
    public decimal PlanType { get; set; }

    [Display(Name = "当前状态")]
    public int CurrentStatus { get; set; }

    public bool CanRenameFabh { get; set; }

    public string? RenameFabhBlockedReason { get; set; }
}

public static class PriceSection
{
    public const string ImportComponents = "import";
    public const string FillPrice = "fill";
}

public class QuotationExcelSaveRequest
{
    public string QuotationNo { get; set; } = string.Empty;
    public List<List<string?>> Rows { get; set; } = [];
}

public class QuotationPlanSaveRequest
{
    public string Fabh { get; set; } = string.Empty;
    public List<List<string?>> TableJson { get; set; } = [];
    public List<string?> TreeNodeNames { get; set; } = [];
}

public class AutoFillPriceRequest
{
    public string Fabh { get; set; } = string.Empty;
}

public class QuotationProjectSummarySaveRequest
{
    public List<QuotationProjectSummaryUpdateItem> Items { get; set; } = [];
}

public class QuotationProjectSummaryUpdateItem
{
    public string MatchKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string OldUnit { get; set; } = string.Empty;
    public decimal OldPrice { get; set; }
    public decimal OldFloatRate { get; set; }
    public string OldVendor { get; set; } = string.Empty;
    public string NewUnit { get; set; } = string.Empty;
    public decimal NewPrice { get; set; }
    public decimal NewFloatRate { get; set; }
    public string NewVendor { get; set; } = string.Empty;
}

public class MergedExcelExportRequest
{
    public List<List<string?>> Rows { get; set; } = [];
}

internal class BjbWriteRow
{
    public string Xbm { get; set; } = string.Empty;
    public string Xmc { get; set; } = string.Empty;
    public string Xggxh { get; set; } = string.Empty;
    public string Xsccj { get; set; } = string.Empty;
    public string Xwzdh { get; set; } = string.Empty;
    public decimal Xdj { get; set; }
    public decimal Xsl { get; set; }
    public decimal Xfdds { get; set; }
    public decimal XbjDj { get; set; }
    public decimal XbjbBj { get; set; }
    public decimal XbjbDj { get; set; }
    public int Xlx { get; set; }
}

internal class SourceUnitBlock
{
    public int SplitCount { get; set; } = 1;
    public List<ComponentSourceRow> Components { get; set; } = [];
}

internal class ComponentSourceRow
{
    public string Name { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Qty { get; set; }
}

internal class SummaryMatchKey
{
    public List<string> Codes { get; set; } = [];
    public string Name { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal FloatRate { get; set; }
    public string Vendor { get; set; } = string.Empty;
}

internal class CabinetReferenceBjRow
{
    public decimal? RefBj { get; set; }
}

internal class AutoFillPriceRow
{
    public string Wzdh { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Vendor { get; set; } = string.Empty;
}

internal class StdPriceHistoryDto
{
    public string Wzdh { get; set; } = string.Empty;
    public decimal LastPrice { get; set; }
    public decimal? AvgPrice { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int AvgCount { get; set; }
}

public class SaveLeafItemFieldsRequest
{
    public string QuotationNo { get; set; } = string.Empty;
    public List<LeafItemUpdateRow> Items { get; set; } = [];
}

public class LeafItemUpdateRow
{
    public string Code { get; set; } = string.Empty;
    public string? Spec { get; set; }
    public string? Unit { get; set; }
    public decimal Price { get; set; }
    public decimal Qty { get; set; }
    public decimal FloatRate { get; set; }
    public string? Vendor { get; set; }
}
