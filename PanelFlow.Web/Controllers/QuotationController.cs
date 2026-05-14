using Microsoft.AspNetCore.Mvc;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Core.Services;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Web.Extensions;
using PanelFlow.Web.Filters;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Globalization;

namespace PanelFlow.Web.Controllers;

[RoleAuthorize(RoleNames.Admin, RoleNames.Quoter, RoleNames.ProductionManager)]
public class QuotationController : Controller
{
    private readonly IQuotationService _quotationService;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<QuotationController> _logger;

    public QuotationController(
        IQuotationService quotationService,
        ApplicationDbContext db,
        ILogger<QuotationController> logger)
    {
        _quotationService = quotationService;
        _db = db;
        _logger = logger;
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

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        ViewData["Title"] = "编辑报价单";
        ViewData["BreadcrumbTitle"] = "编辑报价单";

        var dto = await _quotationService.GetByQuotationNoAsync(id);
        if (dto == null)
        {
            TempData["ErrorMessage"] = "报价单不存在";
            return RedirectToAction(nameof(Index));
        }

        return View(await ToEditModelAsync(dto));
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
        return View(viewModel);
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
        var treeNodes = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh.Trim() == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim()
            })
            .Where(x => x.Code.Length == 4 && x.Code != "9999")
            .OrderBy(x => x.Code)
            .Select(x => new QuotationTreeNodeViewModel
            {
                Code = x.Code,
                Name = string.IsNullOrWhiteSpace(x.Name) ? x.Code : x.Name
            })
            .ToListAsync();

        var viewModel = new QuotationPriceViewModel
        {
            QuotationNo = quotationNo,
            QuotationName = (dto.QuotationName ?? string.Empty).Trim(),
            CurrentStatus = dto.CurrentStatus,
            ActiveSection = activeSection,
            TreeNodes = treeNodes
        };
        return viewModel;
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
            .Where(x => x.fabh.Trim() == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Unit = (x.x_dw ?? string.Empty).Trim(),
                Price = x.x_dj,
                Qty = x.x_sl,
                FloatRate = x.x_fdds,
                Vendor = (x.x_sccj ?? string.Empty).Trim()
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
                x_mc = x.Name,
                x_ggxh = x.Spec,
                x_dw = x.Unit,
                x_dj = x.Price ?? 0m,
                x_sl = x.Qty ?? 0m,
                x_fdds = x.FloatRate ?? 0m,
                x_sccj = x.Vendor
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

    [HttpGet]
    public async Task<IActionResult> GetProjectComponentSummary(string id)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo))
            return BadRequest(new { success = false, message = "报价单编号不能为空" });

        var rows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh.Trim() == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Unit = (x.x_dw ?? string.Empty).Trim(),
                Price = x.x_dj ?? 0m,
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

    [HttpGet]
    public async Task<IActionResult> GetProjectComponentUsage(
        string id,
        string name,
        string spec,
        string unit,
        decimal price,
        decimal floatRate,
        string vendor)
    {
        var quotationNo = (id ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(quotationNo))
            return BadRequest(new { success = false, message = "报价单编号不能为空" });

        var rows = await _db.BjbItems
            .AsNoTracking()
            .Where(x => x.fabh.Trim() == quotationNo)
            .Select(x => new
            {
                Code = (x.x_bm ?? string.Empty).Trim(),
                Name = (x.x_mc ?? string.Empty).Trim(),
                Spec = (x.x_ggxh ?? string.Empty).Trim(),
                Unit = (x.x_dw ?? string.Empty).Trim(),
                Price = x.x_dj ?? 0m,
                Qty = x.x_sl ?? 0m,
                FloatRate = x.x_fdds ?? 0m,
                Vendor = (x.x_sccj ?? string.Empty).Trim()
            })
            .ToListAsync();

        var cabinetNames = rows
            .Where(x => x.Code.Length == 4 && x.Code != "9999")
            .ToDictionary(x => x.Code, x => string.IsNullOrWhiteSpace(x.Name) ? x.Code : x.Name);

        var usage = rows
            .Where(x => x.Code.Length == 12
                        && x.Code.Substring(4, 4) == "0001"
                        && x.Name == (name ?? string.Empty).Trim()
                        && x.Spec == (spec ?? string.Empty).Trim()
                        && x.Unit == (unit ?? string.Empty).Trim()
                        && x.Price == price
                        && x.FloatRate == floatRate
                        && x.Vendor == (vendor ?? string.Empty).Trim())
            .GroupBy(x => x.Code[..4])
            .Select(g => new
            {
                unitCode = g.Key,
                unitName = cabinetNames.TryGetValue(g.Key, out var unitName) ? unitName : g.Key,
                qty = g.Sum(x => x.Qty)
            })
            .OrderBy(x => x.unitCode)
            .ToList();

        return Ok(new { success = true, rows = usage });
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
WHERE LTRIM(RTRIM(fabh)) = {{0}}
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
WHERE LTRIM(RTRIM(fabh)) = {quotationNo}
  AND LTRIM(RTRIM(x_bm)) = {trimmedCode}");
                    }

                    affected += affectedByCodes;
                    continue;
                }

                var matchedCountSql = await _db.Database.SqlQueryRaw<int>(@"
SELECT COUNT(1)
FROM BJB
WHERE LTRIM(RTRIM(fabh)) = {0}
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
WHERE LTRIM(RTRIM(fabh)) = {quotationNo}
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
            _logger.LogError(ex, "读取报价导入Excel失败。QuotationNo={QuotationNo}, FileName={FileName}", quotationNo, file.FileName);
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
            .FirstOrDefaultAsync(x => x.fabh.Trim() == fabh);
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

        var rowsToInsert = BuildRowsFromTable(tableRows, treeNodeNames);
        if (rowsToInsert.Count == 0)
            return BadRequest(new { success = false, message = "未解析到可保存的目录/元件数据" });

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
({fabh}, {row.Xbm}, {row.Xmc}, {string.Empty}, {row.Xdj}, {0m}, {row.Xsl}, {0m}, {row.XbjDj}, {row.XbjbBj}, {row.XbjbDj},
 NULL, {0m}, {0m}, {string.Empty}, {row.Xggxh}, {row.Xsccj}, {string.Empty}, {0m}, {string.Empty}, {string.Empty}, {row.Xlx}, {1})");
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

    private static List<BjbWriteRow> BuildRowsFromTable(List<List<string?>> tableRows, List<string> treeNodeNames)
    {
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
                    Xmc = Limit(unitNodeName, 50),
                    Xggxh = string.Empty,
                    Xsccj = string.Empty,
                    Xdj = 0m,
                    Xsl = 1m,
                    XbjDj = 0m,
                    XbjbBj = 0m,
                    XbjbDj = 0m,
                    Xlx = 1
                });

                result.Add(CreateFixedNode(currentUnitCode, "0001", "器件", 1));
                result.Add(CreateFixedNode(currentUnitCode, "0002", "辅料", 12));
                result.Add(CreateFixedNode(currentUnitCode, "0003", "壳体", 13));
                result.Add(CreateFixedNode(currentUnitCode, "0004", "安装", 14));
                result.Add(CreateFixedNode(currentUnitCode, "0005", "包装", 15));

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

    private static List<SourceUnitBlock> ParseSourceUnits(List<List<string?>> tableRows)
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
                Name = Limit(c3Name, 50),
                Spec = Limit(c4Spec, 50),
                Vendor = Limit(c7Vendor, 50),
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

    private static string Limit(string? value, int maxLen)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= maxLen ? text : text[..maxLen];
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(QuotationEditViewModel model)
    {
        ModelState.Remove(nameof(QuotationEditViewModel.CreatedAt));

        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "编辑报价单";
            ViewData["BreadcrumbTitle"] = "编辑报价单";
            return View(model);
        }

        if (string.IsNullOrWhiteSpace(model.CustomerName) || string.IsNullOrWhiteSpace(model.CustomerAlias))
        {
            ModelState.AddModelError(string.Empty, "请先通过关键字搜索并选择客户");
            ViewData["Title"] = "编辑报价单";
            ViewData["BreadcrumbTitle"] = "编辑报价单";
            return View(model);
        }

        var before = await _quotationService.GetByQuotationNoAsync(model.QuotationNo);
        if (before == null)
        {
            TempData["ErrorMessage"] = "报价单不存在";
            return RedirectToAction(nameof(Index));
        }

        var (success, message) = await _quotationService.UpdateAsync(ToEditDto(model, before));
        if (!success)
        {
            ModelState.AddModelError(string.Empty, message);
            ViewData["Title"] = "编辑报价单";
            ViewData["BreadcrumbTitle"] = "编辑报价单";
            return View(model);
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index));
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

    private async Task<QuotationEditViewModel> ToEditModelAsync(QuotationDto dto)
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

        return model;
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
}

public static class PriceSection
{
    public const string ImportComponents = "import";
    public const string FillPrice = "fill";
}

public class QuotationTreeNodeViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class QuotationPriceViewModel
{
    public string QuotationNo { get; set; } = string.Empty;
    public string QuotationName { get; set; } = string.Empty;
    public int CurrentStatus { get; set; }
    public string ActiveSection { get; set; } = PriceSection.ImportComponents;
    public List<QuotationTreeNodeViewModel> TreeNodes { get; set; } = [];
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

internal class BjbWriteRow
{
    public string Xbm { get; set; } = string.Empty;
    public string Xmc { get; set; } = string.Empty;
    public string Xggxh { get; set; } = string.Empty;
    public string Xsccj { get; set; } = string.Empty;
    public decimal Xdj { get; set; }
    public decimal Xsl { get; set; }
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
