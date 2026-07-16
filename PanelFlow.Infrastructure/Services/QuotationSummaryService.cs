using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Core.Rules;
using PanelFlow.Core.Utilities;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;
using PanelFlow.Infrastructure.Services.QuotationSummary;

namespace PanelFlow.Infrastructure.Services;

public class QuotationSummaryService : IQuotationSummaryService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<QuotationSummaryService> _logger;

    public QuotationSummaryService(ApplicationDbContext db, ILogger<QuotationSummaryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<QuotationSummaryPrecheckResult> PrecheckAsync(string fabh, CancellationToken cancellationToken = default)
    {
        var trimmed = fabh.Trim();
        var bm = await _db.BjbItems.AsNoTracking()
            .Where(x => x.fabh == trimmed && x.x_lx == 11 && x.x_mc == "" && x.x_ggxh == "")
            .Select(x => x.x_bm)
            .FirstOrDefaultAsync(cancellationToken);

        return new QuotationSummaryPrecheckResult
        {
            HasEmptyComponent = !string.IsNullOrWhiteSpace(bm),
            EmptyComponentBm = bm?.Trim()
        };
    }

    public async Task<QuotationSummaryPageDto?> GetPageAsync(string fabh, string loginUserName, CancellationToken cancellationToken = default)
    {
        var trimmed = fabh.Trim();
        var header = await _db.BjfatQuotations.AsNoTracking()
            .FirstOrDefaultAsync(q => q.fabh == trimmed, cancellationToken);
        if (header is null)
            return null;

        var status = await GetStatusAsync(trimmed, cancellationToken);
        return new QuotationSummaryPageDto
        {
            Fabh = trimmed,
            QuotationName = header.famc?.Trim(),
            Status = status,
            CanEdit = QuotationEditRules.CanOwnerOperate(header.bjr?.Trim() ?? string.Empty, header.dqzt, loginUserName)
        };
    }

    public async Task<QuotationSummaryStatusDto> GetStatusAsync(string fabh, CancellationToken cancellationToken = default)
    {
        var trimmed = fabh.Trim();
        var hzbCount = await _db.BjbHzbItems.AsNoTracking().CountAsync(x => x.FABH == trimmed, cancellationToken);
        var xmyjbCount = await _db.BjbXmyjbItems.AsNoTracking().CountAsync(x => x.fabh == trimmed, cancellationToken);
        var xmyjhzCount = await _db.BjbXmyjhzItems.AsNoTracking().CountAsync(x => x.fabh == trimmed, cancellationToken);
        var xmhzCount = await _db.BjbXmhzItems.AsNoTracking()
            .CountAsync(x => x.fabh == trimmed, cancellationToken);

        return new QuotationSummaryStatusDto
        {
            HasHzb = hzbCount > 0,
            HasXmyjb = xmyjbCount > 0,
            HasXmyjhz = xmyjhzCount > 0,
            HasXmhz = xmhzCount > 0,
            HzbCount = hzbCount,
            XmyjbCount = xmyjbCount,
            XmyjhzCount = xmyjhzCount,
            XmhzCount = xmhzCount
        };
    }

    public async Task<QuotationSummaryRunResult> RunSummaryAsync(
        string fabh,
        bool ignoreEmptyComponentWarning,
        Func<string, Task>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        var trimmed = fabh.Trim();
        try
        {
            await ReportAsync(reportProgress, "【前置检查】校验元件名称与规格", cancellationToken);

            var precheck = await PrecheckAsync(trimmed, cancellationToken);
            if (precheck.HasEmptyComponent && !ignoreEmptyComponentWarning)
            {
                return new QuotationSummaryRunResult
                {
                    Success = false,
                    Stage = "Precheck",
                    Message = $"编号:{precheck.EmptyComponentBm}的名称和规格型号不能都为空！是否继续(不能汇总完全)?"
                };
            }

            await ReportAsync(reportProgress, "【初始化】清除旧汇总数据并清空非元件分类字段", cancellationToken);
            await RunSummaryInitAsync(trimmed, cancellationToken);

            await ReportAsync(reportProgress, "【报价汇总】开始计算项目价格与分类汇总", cancellationToken);
            var stageC = await RunStageCAsync(trimmed, reportProgress, cancellationToken);
            if (!stageC.Success)
                return stageC;

            await ReportAsync(reportProgress, "【按柜汇总】开始按控制柜聚合元件", cancellationToken);
            var stageD = await RunStageDAsync(trimmed, reportProgress, cancellationToken);
            if (!stageD.Success)
                return stageD;

            await ReportAsync(reportProgress, "【分类统计】开始项目元件分类汇总", cancellationToken);
            var stageE = await RunStageEAsync(trimmed, reportProgress, cancellationToken);
            if (!stageE.Success)
                return stageE;

            await ReportAsync(reportProgress, "【完成】全部汇总步骤已执行完毕", cancellationToken);

            return new QuotationSummaryRunResult
            {
                Success = true,
                Message = "器件分类汇总成功!"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunSummary failed for {Fabh}", trimmed);
            return new QuotationSummaryRunResult
            {
                Success = false,
                Message = $"汇总失败：{ex.Message}"
            };
        }
    }

    private static async Task ReportAsync(Func<string, Task>? reporter, string message, CancellationToken ct)
    {
        if (reporter is null)
            return;

        ct.ThrowIfCancellationRequested();
        await reporter(message);
    }

    private async Task<QuotationSummaryRunResult> RunStageCAsync(string fabh, Func<string, Task>? reportProgress, CancellationToken ct)
    {
        var hzb = await LoadHzbRowsAsync(fabh, ct);
        if (hzb.Count == 0)
            return Fail("Hz1", "报价单无明细数据");

        var bzb = await _db.BjdBzbItems.AsNoTracking().OrderBy(x => x.XH).ToListAsync(ct);
        var wyb = await _db.BjdWybItems.AsNoTracking().OrderBy(x => x.XH).ToListAsync(ct);
        var engine = new QuotationSummaryHzEngine(bzb, wyb);

        if (hzb.Count > 2)
        {
            await ReportAsync(reportProgress, "【报价汇总】计算项目价格（hz1）并回写 BJB", ct);
            if (!engine.TryHz1(hzb, out var hz1Error))
                return Fail("Hz1", hz1Error ?? "项目价格汇总失败");
            await UpdateBjbFromHzbAsync(fabh, hzb, ct);
        }

        await ReportAsync(reportProgress, "【报价汇总】生成分类汇总框架", ct);
        var flhz = new List<FlhzWorkRow>();
        if (!engine.TryBuildFlhzFramework(hzb, flhz, out var frameError))
            return Fail("FlhzkjSc", frameError ?? "分类汇总框架生成失败");

        await ReportAsync(reportProgress, "【报价汇总】计算分类价格（hz2）", ct);
        if (!engine.TryHz2(flhz, out var hz2Error))
            return Fail("Hz2", hz2Error ?? "分类汇总计算失败");

        var saveTotal = Math.Max(flhz.Count - 1, 0);
        for (var i = 0; i < flhz.Count - 1; i++)
        {
            var row = flhz[i];
            if (i == 0 || i == saveTotal - 1 || (i + 1) % 20 == 0)
                await ReportAsync(reportProgress, $"【报价汇总】写入 BJB_HZB：{i + 1}/{saveTotal}", ct);
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                EXEC BJB_YJHZ_SAVE1
                    @BJDBH={fabh},
                    @X1={row.Xbm}, @X2={row.Xmc}, @X3={row.Xsm},
                    @X4={row.Xlx}, @X5={row.Xsl}, @X6={row.Xzj1},
                    @X7={row.Xzj1Bj}, @X8={row.Xzj1Jj}, @X9={row.Xzj1Scj}, @X10={row.Xzj1Zdj},
                    @X11={row.Xzj2}, @X12={row.Xzj2Zdj}, @X13={row.Xzj3}, @X14={row.Xzj3Zdj},
                    @X15={row.Xzj4}, @X16={row.Xzj4Zdj}, @X17={row.Xzj5}, @X18={row.Xzj5Zdj},
                    @X19={row.Xzj6}, @X20={row.Xzj6Zdj}, @X21={row.Xzj7}, @X22={row.Xzj7Zdj},
                    @X23={row.Xzj8}, @X24={row.Xzj8Zdj}, @X25={row.Xzj9}, @X26={row.Xzj9Zdj},
                    @X27={row.Xzj10}, @X28={row.Xzj10Zdj}, @X29={row.Xwzfy}, @X30={row.Xcgf},
                    @X31={row.Xflbh}, @X32={row.Xggxh}, @X33={row.Xsccj}, @X34={row.XkeyRy}");
        }

        await ReportAsync(reportProgress, "【报价汇总】复制总计行（x_bm=9999）", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO BJB_HZB
            SELECT FABH, '9999', x_mc, x_sm, x_lx, x_sl,
                   x_zj1, x_zj1_bj, x_zj1_jj, x_zj1_scj, x_zj1_zdj,
                   x_zj2, x_zj2_zdj, x_zj3, x_zj3_zdj, x_zj4, x_zj4_zdj,
                   x_zj5, x_zj5_zdj, x_zj6, x_zj6_zdj, x_zj7, x_zj7_zdj,
                   x_zj8, x_zj8_zdj, x_zj9, x_zj9_zdj, x_zj10, x_zj10_zdj,
                   x_wzfy, x_cgf, x_flbh, x_ggxh, x_sccj, x_key_ry
            FROM BJB_HZB
            WHERE fabh = {fabh} AND x_bm = '0'");

        return new QuotationSummaryRunResult { Success = true, Message = "报价单本身汇总完成" };
    }

    private async Task<QuotationSummaryRunResult> RunStageDAsync(string fabh, Func<string, Task>? reportProgress, CancellationToken ct)
    {
        await EnsureSqlServerSessionAsync(ct);

        var specRows = await _db.BjbHzbItems.AsNoTracking()
            .Where(x => x.FABH == fabh && x.x_lx == 11)
            .OrderBy(x => x.x_bm)
            .ToListAsync(ct);

        await ReportAsync(reportProgress, $"【按柜汇总】标准化规格型号（{specRows.Count} 条）", ct);
        foreach (var row in specRows)
        {
            var normalized = QuotationSummarySpecHelper.NormalizeGgxh(row.x_ggxh, row.x_mc);
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE BJB_HZB SET x_ggxh = {normalized}
                WHERE FABH = {fabh} AND x_bm = {row.x_bm}");
        }

        var srcRows = await _db.BjbHzbItems.AsNoTracking()
            .Where(x => x.FABH == fabh
                        && x.x_bm != "0"
                        && x.x_bm != "9999"
                        && (x.x_lx == 11 || (x.x_lx == 1 && x.x_bm.Trim().Length == 4)))
            .OrderBy(x => x.x_sm).ThenBy(x => x.x_bm)
            .ToListAsync(ct);

        if (srcRows.Count == 0)
            return new QuotationSummaryRunResult { Success = true, Message = "无按柜汇总源数据" };

        await ReportAsync(reportProgress, $"【按柜汇总】聚合元件（源数据 {srcRows.Count} 条）", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM BJB_XMYJB WHERE fabh = {fabh}");

        var targets = new List<BjbXmyjbItem>();
        var curSxh = 1;
        var curDymc = string.Empty;
        var curDybm = string.Empty;
        var curRowIndex = -1;

        for (var i = 0; i < srcRows.Count; i++)
        {
            var src = srcRows[i];
            var matched = false;

            if (curRowIndex >= 0)
            {
                for (var j = curRowIndex; j >= 0; j--)
                {
                    var target = targets[j];
                    if (!string.Equals(target.x_dyh.Trim(), (src.x_sm ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                        break;

                    if (string.Equals(target.x_ggxh.Trim(), src.x_ggxh.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(target.x_sccj.Trim(), src.x_sccj.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(target.x_key_ry.Trim(), src.x_key_ry.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        target.x_lx == src.x_lx)
                    {
                        target.x_zsl += src.x_sl;
                        if (src.x_cgf == 0)
                            target.x_bcg_sl += src.x_sl;
                        else
                            target.x_zje += src.x_zj1 * src.x_sl;
                        matched = true;
                        break;
                    }
                }
            }

            if (matched)
                continue;

            if (curDybm != (src.x_sm ?? string.Empty).Trim())
            {
                curDybm = (src.x_sm ?? string.Empty).Trim();
                curDymc = (src.x_mc ?? string.Empty).Trim();
            }

            var item = BuildXmyjbRow(fabh, src, curSxh++, curDymc, curDybm);
            targets.Add(item);
            curRowIndex = targets.Count - 1;
        }

        await ReportAsync(reportProgress, $"【按柜汇总】写入 BJB_XMYJB（{targets.Count} 行）", ct);
        foreach (var item in targets)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO BJB_XMYJB
                (fabh, x_dyh, x_ggxh, x_sccj, x_key_ry, x_lylx, x_flbh, x_qjmc, x_dymc, x_lx,
                 x_zsl, x_zje, x_bcg_sl, x_sxh, x_zxm_sl, x_zxm_je, x_zxm_bcg_sl)
                VALUES
                ({item.fabh}, {item.x_dyh}, {item.x_ggxh}, {item.x_sccj}, {item.x_key_ry}, {item.x_lylx},
                 {item.x_flbh}, {item.x_qjmc}, {item.x_dymc}, {item.x_lx},
                 {item.x_zsl}, {item.x_zje}, {item.x_bcg_sl}, {item.x_sxh},
                 {item.x_zxm_sl}, {item.x_zxm_je}, {item.x_zxm_bcg_sl})");
        }

        var units = await _db.BjbXmyjbItems.AsNoTracking()
            .Where(x => x.fabh == fabh && x.x_lx == 1 && x.x_zsl > 0)
            .OrderBy(x => x.x_dyh)
            .ToListAsync(ct);

        await ReportAsync(reportProgress, $"【按柜汇总】按柜扩展项目总量（{units.Count} 柜）", ct);
        foreach (var unit in units)
        {
            var kk = unit.x_zsl;
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE BJB_XMYJB
                SET x_zxm_sl = x_zsl * {kk},
                    x_zxm_je = x_zje * {kk},
                    x_zxm_bcg_sl = x_bcg_sl * {kk}
                WHERE fabh = {fabh} AND x_dyh = {unit.x_dyh} AND x_lx = 11");
        }

        return new QuotationSummaryRunResult { Success = true, Message = "按柜元件汇总完成" };
    }

    private static BjbXmyjbItem BuildXmyjbRow(string fabh, BjbHzbItem src, int sxh, string curDymc, string curDybm)
    {
        var item = new BjbXmyjbItem
        {
            fabh = fabh,
            x_dyh = (src.x_sm ?? string.Empty).Trim(),
            x_lylx = 0,
            x_sxh = sxh
        };

        if (src.x_lx == 1)
        {
            item.x_flbh = string.Empty;
            item.x_ggxh = string.Empty;
            item.x_sccj = string.Empty;
            item.x_key_ry = string.Empty;
            item.x_zsl = src.x_sl;
            item.x_bcg_sl = 0;
            item.x_zje = src.x_zj1;
            item.x_zxm_sl = item.x_zsl;
            item.x_zxm_je = item.x_zje;
            item.x_zxm_bcg_sl = 0;
            item.x_qjmc = string.Empty;
            item.x_dymc = (src.x_mc ?? string.Empty).Trim();
            item.x_lx = 1;
            return item;
        }

        item.x_flbh = src.x_flbh.Trim();
        item.x_ggxh = src.x_ggxh.Trim();
        item.x_sccj = src.x_sccj.Trim();
        item.x_key_ry = src.x_key_ry.Trim();
        item.x_zsl = src.x_sl;
        if (src.x_cgf == 0)
        {
            item.x_bcg_sl = src.x_sl;
            item.x_zje = 0;
        }
        else
        {
            item.x_bcg_sl = 0;
            item.x_zje = src.x_zj1 * src.x_sl;
        }

        item.x_qjmc = (src.x_mc ?? string.Empty).Trim();
        item.x_dymc = src.x_bm.Trim().Length == 4 ? item.x_qjmc : curDymc;
        item.x_lx = src.x_lx;
        item.x_zxm_sl = 0;
        item.x_zxm_je = 0;
        item.x_zxm_bcg_sl = 0;

        if (string.IsNullOrWhiteSpace(item.x_flbh) &&
            string.IsNullOrWhiteSpace(item.x_ggxh) &&
            string.IsNullOrWhiteSpace(item.x_sccj) &&
            string.IsNullOrWhiteSpace(item.x_key_ry))
        {
            item.x_ggxh = item.x_qjmc;
        }

        return item;
    }

    private async Task<QuotationSummaryRunResult> RunStageEAsync(string fabh, Func<string, Task>? reportProgress, CancellationToken ct)
    {
        await ReportAsync(reportProgress, "【分类统计】初始化 BJB_XMYJHZ（聚合按柜元件）", ct);
        await ExecSpAsync("BJB_YJHZB_SC4", fabh, ct);
        await EnsureSqlServerSessionAsync(ct);

        await ReportAsync(reportProgress, "【分类统计】回填元件名称与分类编号", ct);
        await BackfillXmyjhzClassificationAsync(fabh, ct);

        var categories = await _db.BjhzbCategoryItems.AsNoTracking()
            .Where(x => x.fabh == 1 && x.x_bh.Trim().Length >= 4)
            .OrderBy(x => x.x_bh)
            .ToListAsync(ct);

        if (categories.Count == 0)
            return Fail("StageE", "项目器件分类汇总表生成失败：分类字典为空");

        await ReportAsync(reportProgress, "【分类统计】按顶级分类聚合写入 BJB_XMHZ", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM BJB_XMHZ WHERE fabh = {fabh}");

        var curTop = categories[0].x_bh.Trim()[..4];
        var curFlmc = categories[0].x_mc.Trim();
        decimal zSl = 0, zJe = 0, zBcgSl = 0;

        for (var i = 1; i < categories.Count; i++)
        {
            var cat = categories[i];
            var catFlbh = cat.x_flbh.Trim();
            if (!string.IsNullOrWhiteSpace(catFlbh))
            {
                var hzflbh = catFlbh + "%";
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE BJB_XMYJHZ SET x_hzjb = {curTop}
                    WHERE fabh = {fabh} AND x_flbh LIKE {hzflbh}");

                var sums = await _db.BjbXmyjhzItems.AsNoTracking()
                    .Where(x => x.fabh == fabh && EF.Functions.Like(x.x_flbh, hzflbh))
                    .GroupBy(_ => 1)
                    .Select(g => new
                    {
                        Sl = g.Sum(x => x.x_sl),
                        Je = g.Sum(x => x.x_je),
                        Bcg = g.Sum(x => x.x_bcg_sl)
                    })
                    .FirstOrDefaultAsync(ct);

                if (sums is not null)
                {
                    zSl += sums.Sl;
                    zJe += sums.Je;
                    zBcgSl += sums.Bcg;
                }
            }

            var nextTop = cat.x_bh.Trim()[..4];
            if (nextTop != curTop)
            {
                await InsertXmhzAsync(fabh, curFlmc, zSl, zJe, zBcgSl, curTop, ct);
                curTop = nextTop;
                curFlmc = cat.x_mc.Trim();
                zSl = 0;
                zJe = 0;
                zBcgSl = 0;
            }
        }

        await InsertXmhzAsync(fabh, curFlmc, zSl, zJe, zBcgSl, curTop, ct);

        await ReportAsync(reportProgress, "【分类统计】处理未知分类", ct);
        var unknownFlbh = (int.TryParse(curTop, out var n) ? (n + 1).ToString("0000") : "9999");
        var unknownSums = await _db.BjbXmyjhzItems.AsNoTracking()
            .Where(x => x.fabh == fabh && (x.x_flbh == "" || x.x_hzjb == ""))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Sl = g.Sum(x => x.x_sl),
                Je = g.Sum(x => x.x_je),
                Bcg = g.Sum(x => x.x_bcg_sl)
            })
            .FirstOrDefaultAsync(ct);

        if (unknownSums is not null && (unknownSums.Sl > 0 || unknownSums.Je > 0 || unknownSums.Bcg > 0))
        {
            await InsertXmhzAsync(fabh, "未知分类", unknownSums.Sl, unknownSums.Je, unknownSums.Bcg, unknownFlbh, ct);
        }

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE BJB_XMYJHZ SET x_hzjb = {unknownFlbh}
            WHERE fabh = {fabh} AND (x_flbh = '' OR x_hzjb = '')");

        return new QuotationSummaryRunResult { Success = true, Message = "项目元件分类统计完成" };
    }

    private Task InsertXmhzAsync(string fabh, string mc, decimal sl, decimal je, decimal bcgSl, string hzjb, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO BJB_XMHZ (fabh, x_flbh, x_ggxh, x_sccj, x_key_ry, x_mc, x_sl, x_je, x_bcg_sl, x_hzjb)
            VALUES ({fabh}, '', '', '', '', {mc}, {sl}, {je}, {bcgSl}, {hzjb})", ct);

    private async Task<List<HzbWorkRow>> LoadHzbRowsAsync(string fabh, CancellationToken ct)
    {
        var rows = await _db.Database.SqlQueryRaw<HzbQueryRow>(@"
            SELECT fabh AS Fabh, x_bm AS Xbm, ISNULL(x_mc,'') AS Xmc, ISNULL(x_dw,'') AS Xdw,
                   ISNULL(x_dj,0) AS Xdj, ISNULL(x_fdds,0) AS Xfdds, ISNULL(x_sl,0) AS Xsl,
                   ISNULL(x_bj_fdds,0) AS XbjFdds, ISNULL(x_bj_dj,0) AS XbjDj,
                   ISNULL(x_bjb_bj,0) AS XbjbBj, ISNULL(x_bjb_dj,0) AS XbjbDj,
                   x_bjb_datetime AS XbjbDatetime, ISNULL(x_bjb_fdds,0) AS XbjbFdds,
                   ISNULL(x_wzfy,0) AS Xwzfy, ISNULL(x_flbh,'') AS Xflbh, ISNULL(x_ggxh,'') AS Xggxh,
                   ISNULL(x_sccj,'') AS Xsccj, ISNULL(x_key_ry,'') AS XkeyRy,
                   ISNULL(x_jsgsbh,0) AS Xjsgsbh, ISNULL(x_bz,'') AS Xbz, ISNULL(x_wzdh,'') AS Xwzdh,
                   ISNULL(x_lx,0) AS Xlx, ISNULL(x_cgf,0) AS Xcgf
            FROM BJB WHERE fabh = {0} ORDER BY x_bm ASC", fabh)
            .ToListAsync(ct);

        return rows.Select(r => new HzbWorkRow
        {
            Fabh = r.Fabh.Trim(),
            Xbm = r.Xbm.Trim(),
            Xmc = r.Xmc.Trim(),
            Xdw = r.Xdw.Trim(),
            Xdj = r.Xdj,
            Xfdds = r.Xfdds,
            Xsl = r.Xsl,
            XbjFdds = r.XbjFdds,
            XbjDj = r.XbjDj,
            XbjbBj = r.XbjbBj,
            XbjbDj = r.XbjbDj,
            XbjbDatetime = r.XbjbDatetime,
            XbjbFdds = r.XbjbFdds,
            Xwzfy = r.Xwzfy,
            Xflbh = r.Xflbh.Trim(),
            Xggxh = r.Xggxh.Trim(),
            Xsccj = r.Xsccj.Trim(),
            XkeyRy = r.XkeyRy.Trim(),
            Xjsgsbh = r.Xjsgsbh,
            Xbz = r.Xbz.Trim(),
            Xwzdh = r.Xwzdh.Trim(),
            Xlx = r.Xlx,
            Xcgf = r.Xcgf
        }).ToList();
    }

    private async Task UpdateBjbFromHzbAsync(string fabh, List<HzbWorkRow> hzb, CancellationToken ct)
    {
        await EnsureSqlServerSessionAsync(ct);
        foreach (var row in hzb)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE BJB SET
                    x_dj = {row.Xdj}, x_fdds = {row.Xfdds}, x_sl = {row.Xsl},
                    x_bj_fdds = {row.XbjFdds}, x_bj_dj = {row.XbjDj},
                    x_bjb_bj = {row.XbjbBj}, x_bjb_dj = {row.XbjbDj},
                    x_bjb_fdds = {row.XbjbFdds}, x_wzfy = {row.Xwzfy}, x_lx = {row.Xlx}
                WHERE fabh = {fabh} AND x_bm = {row.Xbm}");
        }
    }

    /// <summary>
    /// x_flbh 为复合主键之一，须用 SQL 直接 UPDATE（EF 不允许修改键列）。
    /// </summary>
    private Task BackfillXmyjhzClassificationAsync(string fabh, CancellationToken ct)
    {
        var sql = SqlServerLegacySession.PrefixBatch("""
            UPDATE h
            SET x_mc = ISNULL(j.x_qjmc, ''),
                x_flbh = ISNULL(j.x_flbh, '')
            FROM BJB_XMYJHZ h
            OUTER APPLY (
                SELECT TOP 1 j.x_qjmc, j.x_flbh
                FROM BJB_XMYJB j
                WHERE j.fabh = h.fabh
                  AND j.x_ggxh = h.x_ggxh
                  AND j.x_sccj = h.x_sccj
                  AND j.x_key_ry = h.x_key_ry
                ORDER BY j.x_flbh DESC
            ) j
            WHERE h.fabh = @fabh
            """);

        return _db.Database.ExecuteSqlRawAsync(sql, [new SqlParameter("@fabh", fabh)], ct);
    }

    /// <summary>
    /// 等价于 BJB_YJHZ_SCH0。该过程以 ANSI_NULLS/QUOTED_IDENTIFIER OFF 创建，直接 EXEC 会在 UPDATE BJB 时失败。
    /// </summary>
    private Task RunSummaryInitAsync(string fabh, CancellationToken ct)
    {
        var sql = SqlServerLegacySession.PrefixBatch("""
            BEGIN TRAN;
            UPDATE BJB SET x_flbh = '', x_sccj = ''
            WHERE fabh = @fabh AND x_lx <> 11;
            DELETE FROM BJB_HZB WHERE fabh = @fabh;
            DELETE FROM BJB_XMYJHZ WHERE fabh = @fabh;
            DELETE FROM BJB_XMHZ WHERE fabh = @fabh;
            DELETE FROM BJB_XMYJB WHERE fabh = @fabh;
            COMMIT TRAN;
            """);

        return _db.Database.ExecuteSqlRawAsync(sql, [new SqlParameter("@fabh", fabh)], ct);
    }

    private static readonly HashSet<string> AllowedSummaryProcedures = new(StringComparer.Ordinal)
    {
        "BJB_YJHZB_SC4"
    };

    /// <summary>
    /// 过程名须为字面量（不可参数化为 EXEC @p0），且 SET 须与 EXEC 同一批次。
    /// </summary>
    private Task ExecSpAsync(string spName, string fabh, CancellationToken ct)
    {
        if (!AllowedSummaryProcedures.Contains(spName))
            throw new ArgumentException($"Stored procedure not allowed: {spName}", nameof(spName));

        var sql = SqlServerLegacySession.PrefixBatch($"EXEC [{spName}] @BJDBH = @fabh");
        return _db.Database.ExecuteSqlRawAsync(sql, [new SqlParameter("@fabh", fabh)], ct);
    }

    private Task EnsureSqlServerSessionAsync(CancellationToken ct) =>
        _db.Database.ExecuteSqlRawAsync(SqlServerLegacySession.OptionsSql, ct);

    private static QuotationSummaryRunResult Fail(string stage, string message) =>
        new() { Success = false, Stage = stage, Message = message };

    private sealed class HzbQueryRow
    {
        public string Fabh { get; set; } = string.Empty;
        public string Xbm { get; set; } = string.Empty;
        public string Xmc { get; set; } = string.Empty;
        public string Xdw { get; set; } = string.Empty;
        public decimal Xdj { get; set; }
        public decimal Xfdds { get; set; }
        public decimal Xsl { get; set; }
        public decimal XbjFdds { get; set; }
        public decimal XbjDj { get; set; }
        public decimal XbjbBj { get; set; }
        public decimal XbjbDj { get; set; }
        public DateTime? XbjbDatetime { get; set; }
        public decimal XbjbFdds { get; set; }
        public decimal Xwzfy { get; set; }
        public string Xflbh { get; set; } = string.Empty;
        public string Xggxh { get; set; } = string.Empty;
        public string Xsccj { get; set; } = string.Empty;
        public string XkeyRy { get; set; } = string.Empty;
        public decimal Xjsgsbh { get; set; }
        public string Xbz { get; set; } = string.Empty;
        public string Xwzdh { get; set; } = string.Empty;
        public int Xlx { get; set; }
        public int Xcgf { get; set; }
    }
}
