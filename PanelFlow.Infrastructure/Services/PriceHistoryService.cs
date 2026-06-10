using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Services;

public class PriceHistoryService : IPriceHistoryService
{
    /// <summary>最新价与均价偏离超过此比例时标记为异常（与填价页偏离检测口径一致）。</summary>
    private const decimal LastAvgDeviationRatio = 0.2m;
    private const int MaxUnitLength = 10;
    private const int MaxVendorLength = 100;

    private readonly ApplicationDbContext _db;
    private readonly IAuditLogService _auditLogService;

    public PriceHistoryService(ApplicationDbContext db, IAuditLogService auditLogService)
    {
        _db = db;
        _auditLogService = auditLogService;
    }

    public async Task<PriceHistoryListResult> ListHistoryAsync(
        string? keyword, bool onlySuspect, int page, int pageSize, string? sortBy = "ggxh", bool sortAsc = true)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var mapped = await LoadFilteredHistoryRowsAsync(keyword, onlySuspect, sortBy, sortAsc);

        var total = mapped.Count;
        var items = mapped
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PriceHistoryListResult
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<IReadOnlyList<PriceSourceRowDto>> GetSourceRowsAsync(string xWzdh)
    {
        var wzdh = (xWzdh ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(wzdh))
        {
            return [];
        }

        var exclusions = await LoadExclusionMapAsync();
        var fiveYearsAgo = DateTime.Now.AddYears(-5);

        // 历史库字符串列可为 NULL，EF 实体映射为非可空 string 会触发 SqlNullValueException；
        // 使用 SqlQueryRaw + ISNULL，与 QuotationStructureService 读取 BJB 的方式一致。
        var rawRows = await _db.Database.SqlQueryRaw<PriceSourceQueryRow>($@"
SELECT
    LTRIM(RTRIM(ISNULL(b.fabh, ''))) AS Fabh,
    LTRIM(RTRIM(ISNULL(b.x_bm, ''))) AS Xbm,
    LTRIM(RTRIM(ISNULL(b.x_mc, ''))) AS Xmc,
    LTRIM(RTRIM(ISNULL(b.x_ggxh, ''))) AS Xggxh,
    b.x_bj_dj AS XbjDj,
    b.x_sl AS Xsl,
    b.x_bjb_datetime AS XbjbDatetime,
    LTRIM(RTRIM(ISNULL(b.x_wzdh, ''))) AS Xwzdh,
    LTRIM(RTRIM(ISNULL(f.famc, ''))) AS Famc,
    f.dqzt AS Dqzt
FROM BJB b
INNER JOIN BJFAT f ON LTRIM(RTRIM(b.fabh)) = LTRIM(RTRIM(f.fabh))
WHERE b.x_lx = 11
  AND b.x_bj_dj > 0
  AND f.dqzt = 10
  AND b.x_bjb_datetime IS NOT NULL
  AND b.x_bjb_datetime >= {{0}}
  AND LTRIM(RTRIM(ISNULL(b.x_wzdh, ''))) = {{1}}
ORDER BY b.fabh DESC, b.x_bm", fiveYearsAgo, wzdh).ToListAsync();

        return rawRows.Select(r =>
        {
            var fabh = (r.Fabh ?? string.Empty).Trim();
            var isExcluded = IsExcluded(fabh, wzdh, exclusions);
            var (isSuspect, reason) = DetectSourceSuspect(r.XbjDj, r.Xsl);
            return new PriceSourceRowDto
            {
                fabh = fabh,
                famc = (r.Famc ?? string.Empty).Trim(),
                x_bm = (r.Xbm ?? string.Empty).Trim(),
                x_mc = (r.Xmc ?? string.Empty).Trim(),
                x_ggxh = (r.Xggxh ?? string.Empty).Trim(),
                x_bj_dj = r.XbjDj,
                x_sl = r.Xsl,
                x_bjb_datetime = r.XbjbDatetime,
                dqzt = r.Dqzt,
                IsExcluded = isExcluded,
                IsSuspect = isSuspect,
                SuspectReason = reason
            };
        }).ToList();
    }

    public async Task<(bool Success, string Message)> AddExclusionAsync(
        string fabh, string? xWzdh, string reason, string userName)
    {
        fabh = (fabh ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(fabh))
        {
            return (false, "方案编号不能为空");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return (false, "剔除理由不能为空");
        }

        var wzdh = string.IsNullOrWhiteSpace(xWzdh) ? null : xWzdh.Trim();
        var exclusions = await _db.StdPriceExclusions.ToListAsync();

        if (wzdh == null)
        {
            if (exclusions.Any(e => e.fabh.Trim().Equals(fabh, StringComparison.OrdinalIgnoreCase) && e.x_wzdh == null))
            {
                return (false, "该报价单已在整单剔除清单中");
            }

            var perWzdh = exclusions
                .Where(e => e.fabh.Trim().Equals(fabh, StringComparison.OrdinalIgnoreCase) && e.x_wzdh != null)
                .ToList();
            if (perWzdh.Count > 0)
            {
                _db.StdPriceExclusions.RemoveRange(perWzdh);
            }
        }
        else
        {
            if (exclusions.Any(e =>
                    e.fabh.Trim().Equals(fabh, StringComparison.OrdinalIgnoreCase) && e.x_wzdh == null))
            {
                return (false, "该报价单已整单剔除，请先恢复整单剔除后再按型号剔除");
            }

            if (exclusions.Any(e =>
                    e.fabh.Trim().Equals(fabh, StringComparison.OrdinalIgnoreCase)
                    && e.x_wzdh != null
                    && e.x_wzdh.Trim().Equals(wzdh, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, "该来源已在剔除清单中");
            }
        }

        var entity = new StdPriceExclusion
        {
            fabh = fabh,
            x_wzdh = wzdh,
            reason = reason.Trim(),
            created_by = userName,
            created_at = DateTime.Now
        };

        _db.StdPriceExclusions.Add(entity);
        await _db.SaveChangesAsync();

        await _auditLogService.WriteAsync(new AuditLogEntry
        {
            ActionType = "AddPriceExclusion",
            Module = "PriceHistory",
            EntityName = "STD_PRICE_EXCLUSION",
            EntityId = entity.Id.ToString(),
            UserName = userName,
            IsSuccess = true,
            AfterData = JsonSerializer.Serialize(new { fabh, x_wzdh = wzdh, reason = entity.reason })
        });

        return (true, wzdh == null ? "已整单剔除" : "已剔除该型号来源");
    }

    public async Task<(bool Success, string Message)> RemoveExclusionAsync(int id, string userName)
    {
        var entity = await _db.StdPriceExclusions.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null)
        {
            return (false, "剔除记录不存在");
        }

        var before = JsonSerializer.Serialize(new
        {
            entity.fabh,
            entity.x_wzdh,
            entity.reason
        });

        _db.StdPriceExclusions.Remove(entity);
        await _db.SaveChangesAsync();

        await _auditLogService.WriteAsync(new AuditLogEntry
        {
            ActionType = "RemovePriceExclusion",
            Module = "PriceHistory",
            EntityName = "STD_PRICE_EXCLUSION",
            EntityId = id.ToString(),
            UserName = userName,
            IsSuccess = true,
            BeforeData = before
        });

        return (true, "已恢复该来源");
    }

    public async Task<IReadOnlyList<PriceExclusionDto>> ListExclusionsAsync()
    {
        var items = await _db.StdPriceExclusions
            .AsNoTracking()
            .OrderByDescending(x => x.created_at)
            .ThenBy(x => x.fabh)
            .ToListAsync();

        return items.Select(e => new PriceExclusionDto
        {
            Id = e.Id,
            fabh = e.fabh.Trim(),
            x_wzdh = e.x_wzdh?.Trim(),
            reason = e.reason?.Trim(),
            created_by = e.created_by?.Trim(),
            created_at = e.created_at,
            IsWholeQuotation = string.IsNullOrWhiteSpace(e.x_wzdh)
        }).ToList();
    }

    public async Task<(bool Success, string Message)> RefreshHistoryAsync(string userName)
    {
        var previousTimeout = _db.Database.GetCommandTimeout();
        try
        {
            _db.Database.SetCommandTimeout(300);
            await _db.Database.ExecuteSqlRawAsync("EXEC [dbo].[SP_RefreshPriceHistory]");

            await _auditLogService.WriteAsync(new AuditLogEntry
            {
                ActionType = "RefreshPriceHistory",
                Module = "PriceHistory",
                EntityName = "STD_PRICE_HISTORY",
                UserName = userName,
                IsSuccess = true,
                AfterData = JsonSerializer.Serialize(new { refreshedAt = DateTime.Now })
            });

            return (true, "历史价格已重新生成");
        }
        catch (Exception ex)
        {
            await _auditLogService.WriteAsync(new AuditLogEntry
            {
                ActionType = "RefreshPriceHistory",
                Module = "PriceHistory",
                EntityName = "STD_PRICE_HISTORY",
                UserName = userName,
                IsSuccess = false,
                ErrorMessage = ex.Message
            });

            return (false, $"重新生成失败：{ex.Message}");
        }
        finally
        {
            _db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    public async Task<(bool Success, string Message)> UpdateAttributesAsync(
        IReadOnlyList<PriceHistoryAttributeUpdateItem> items, string userName)
    {
        if (items == null || items.Count == 0)
        {
            return (false, "没有要保存的数据");
        }

        var ids = items.Select(x => x.Id).Distinct().ToList();
        var entities = await _db.StdPriceHistories
            .Where(h => ids.Contains(h.Id))
            .ToListAsync();

        if (entities.Count == 0)
        {
            return (false, "记录不存在");
        }

        var entityMap = entities.ToDictionary(x => x.Id);
        var changes = new List<object>();

        foreach (var item in items)
        {
            if (!entityMap.TryGetValue(item.Id, out var entity))
            {
                continue;
            }

            var (dw, dwErr) = NormalizeOptionalField(item.x_dw, MaxUnitLength, "单位");
            if (dwErr != null)
            {
                return (false, dwErr);
            }

            var (sccj, sccjErr) = NormalizeOptionalField(item.x_sccj, MaxVendorLength, "厂商");
            if (sccjErr != null)
            {
                return (false, sccjErr);
            }

            var before = new { entity.x_dw, entity.x_sccj };
            entity.x_dw = dw;
            entity.x_sccj = sccj;
            entity.updated_at = DateTime.Now;
            changes.Add(new
            {
                entity.Id,
                entity.x_wzdh,
                before,
                after = new { entity.x_dw, entity.x_sccj }
            });
        }

        if (changes.Count == 0)
        {
            return (false, "没有有效的更新项");
        }

        await _db.SaveChangesAsync();

        await _auditLogService.WriteAsync(new AuditLogEntry
        {
            ActionType = "UpdatePriceHistoryAttributes",
            Module = "PriceHistory",
            EntityName = "STD_PRICE_HISTORY",
            UserName = userName,
            IsSuccess = true,
            AfterData = JsonSerializer.Serialize(changes)
        });

        return (true, $"已保存 {changes.Count} 条");
    }

    public async Task<(bool Success, string Message, int AffectedCount)> BatchUpdateAttributesAsync(
        PriceHistoryBatchUpdateRequest request, string userName)
    {
        request ??= new PriceHistoryBatchUpdateRequest();

        var hasDw = request.x_dw != null;
        var hasSccj = request.x_sccj != null;
        if (!hasDw && !hasSccj)
        {
            return (false, "请至少填写单位或厂商", 0);
        }

        string? newDw = null;
        string? newSccj = null;

        if (hasDw)
        {
            var (dw, dwErr) = NormalizeOptionalField(request.x_dw, MaxUnitLength, "单位");
            if (dwErr != null)
            {
                return (false, dwErr, 0);
            }

            newDw = dw;
        }

        if (hasSccj)
        {
            var (sccj, sccjErr) = NormalizeOptionalField(request.x_sccj, MaxVendorLength, "厂商");
            if (sccjErr != null)
            {
                return (false, sccjErr, 0);
            }

            newSccj = sccj;
        }

        var matched = await LoadFilteredHistoryRowsAsync(request.Keyword, request.OnlySuspect);
        if (matched.Count == 0)
        {
            return (false, "当前筛选条件下没有匹配记录", 0);
        }

        var ids = matched.Select(x => x.Id).ToList();
        var entities = await _db.StdPriceHistories
            .Where(h => ids.Contains(h.Id))
            .ToListAsync();

        var now = DateTime.Now;
        foreach (var entity in entities)
        {
            if (hasDw)
            {
                entity.x_dw = newDw;
            }

            if (hasSccj)
            {
                entity.x_sccj = newSccj;
            }

            entity.updated_at = now;
        }

        await _db.SaveChangesAsync();

        await _auditLogService.WriteAsync(new AuditLogEntry
        {
            ActionType = "BatchUpdatePriceHistoryAttributes",
            Module = "PriceHistory",
            EntityName = "STD_PRICE_HISTORY",
            UserName = userName,
            IsSuccess = true,
            AfterData = JsonSerializer.Serialize(new
            {
                request.Keyword,
                request.OnlySuspect,
                x_dw = newDw,
                x_sccj = newSccj,
                affectedCount = entities.Count
            })
        });

        return (true, $"已批量更新 {entities.Count} 条", entities.Count);
    }

    private async Task<List<PriceHistoryRowDto>> LoadFilteredHistoryRowsAsync(
        string? keyword, bool onlySuspect, string? sortBy = null, bool sortAsc = true)
    {
        var query = _db.StdPriceHistories.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(h =>
                h.x_wzdh.Contains(kw) ||
                (h.ggxh != null && h.ggxh.Contains(kw)) ||
                (h.x_mc != null && h.x_mc.Contains(kw)) ||
                (h.x_dw != null && h.x_dw.Contains(kw)) ||
                (h.x_sccj != null && h.x_sccj.Contains(kw)) ||
                (h.last_fabh != null && h.last_fabh.Contains(kw)));
        }

        var allRows = await query.ToListAsync();
        var mapped = allRows.Select(ToHistoryRowDto).ToList();

        if (onlySuspect)
        {
            mapped = mapped.Where(x => x.IsSuspect).ToList();
        }

        if (sortBy != null)
        {
            mapped = ApplySort(mapped, sortBy, sortAsc);
        }

        return mapped;
    }

    private static (string? Value, string? Error) NormalizeOptionalField(
        string? value, int maxLength, string fieldLabel)
    {
        if (value == null)
        {
            return (null, null);
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return (null, null);
        }

        if (trimmed.Length > maxLength)
        {
            return (null, $"{fieldLabel}不能超过 {maxLength} 个字符");
        }

        return (trimmed, null);
    }

    private async Task<List<StdPriceExclusion>> LoadExclusionMapAsync()
    {
        return await _db.StdPriceExclusions.AsNoTracking().ToListAsync();
    }

    private static bool IsExcluded(string fabh, string wzdh, List<StdPriceExclusion> exclusions)
    {
        foreach (var e in exclusions)
        {
            if (!e.fabh.Trim().Equals(fabh, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (e.x_wzdh == null)
            {
                return true;
            }

            if (e.x_wzdh.Trim().Equals(wzdh, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private PriceHistoryRowDto ToHistoryRowDto(StdPriceHistory h)
    {
        var dto = new PriceHistoryRowDto
        {
            Id = h.Id,
            x_wzdh = h.x_wzdh.Trim(),
            ggxh = h.ggxh?.Trim(),
            x_mc = h.x_mc?.Trim(),
            x_dw = h.x_dw?.Trim(),
            x_sccj = h.x_sccj?.Trim(),
            last_price = h.last_price,
            last_fabh = h.last_fabh?.Trim(),
            last_date = h.last_date,
            avg_price = h.avg_price,
            avg_count = h.avg_count,
            min_price = h.min_price,
            max_price = h.max_price,
            updated_at = h.updated_at
        };

        dto.DeviationPercent = ComputeDeviationPercent(dto.last_price, dto.avg_price);
        var (isSuspect, reason) = DetectHistorySuspect(dto.last_price, dto.avg_price);
        dto.IsSuspect = isSuspect;
        dto.SuspectReason = reason;
        return dto;
    }

    private static decimal? ComputeDeviationPercent(decimal lastPrice, decimal? avgPrice)
    {
        if (avgPrice is null or <= 0)
        {
            return null;
        }

        return Math.Round((lastPrice - avgPrice.Value) / avgPrice.Value * 100m, 1);
    }

    private static List<PriceHistoryRowDto> ApplySort(
        List<PriceHistoryRowDto> items, string? sortBy, bool sortAsc)
    {
        var key = (sortBy ?? "ggxh").Trim().ToLowerInvariant();
        IEnumerable<PriceHistoryRowDto> sorted = key switch
        {
            "x_mc" => sortAsc
                ? items.OrderBy(x => x.x_mc ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(x => x.x_mc ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            "last_price" => sortAsc
                ? items.OrderBy(x => x.last_price)
                : items.OrderByDescending(x => x.last_price),
            "avg_price" => sortAsc
                ? items.OrderBy(x => x.avg_price ?? decimal.MaxValue)
                : items.OrderByDescending(x => x.avg_price ?? decimal.MinValue),
            "min_price" => sortAsc
                ? items.OrderBy(x => x.min_price ?? decimal.MaxValue)
                : items.OrderByDescending(x => x.min_price ?? decimal.MinValue),
            "max_price" => sortAsc
                ? items.OrderBy(x => x.max_price ?? decimal.MaxValue)
                : items.OrderByDescending(x => x.max_price ?? decimal.MinValue),
            "avg_count" => sortAsc
                ? items.OrderBy(x => x.avg_count)
                : items.OrderByDescending(x => x.avg_count),
            "deviation" => sortAsc
                ? items.OrderBy(x => x.DeviationPercent ?? decimal.MaxValue)
                : items.OrderByDescending(x => x.DeviationPercent ?? decimal.MinValue),
            "last_fabh" => sortAsc
                ? items.OrderBy(x => x.last_fabh ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(x => x.last_fabh ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            "x_dw" => sortAsc
                ? items.OrderBy(x => x.x_dw ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(x => x.x_dw ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            "x_sccj" => sortAsc
                ? items.OrderBy(x => x.x_sccj ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(x => x.x_sccj ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            _ => sortAsc
                ? items.OrderBy(x => x.ggxh ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(x => x.ggxh ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        };

        return sorted.ToList();
    }

    private static (bool IsSuspect, string? Reason) DetectHistorySuspect(decimal lastPrice, decimal? avgPrice)
    {
        if (avgPrice is null or <= 0)
        {
            return (false, null);
        }

        var deviation = Math.Abs(lastPrice - avgPrice.Value) / avgPrice.Value;
        if (deviation > LastAvgDeviationRatio)
        {
            var pct = (int)(LastAvgDeviationRatio * 100);
            return (true, $"最新价 ¥{lastPrice:F2} 偏离均价 ¥{avgPrice:F2} 超过 {pct}%");
        }

        return (false, null);
    }

    private static (bool IsSuspect, string? Reason) DetectSourceSuspect(decimal? price, decimal? qty)
    {
        if (price is > 0 && qty is > 0 && Math.Abs(price.Value - qty.Value) < 0.01m)
        {
            return (true, "单价与数量相同，疑似数量误填为单价");
        }

        return (false, null);
    }

    private sealed class PriceSourceQueryRow
    {
        public string Fabh { get; set; } = string.Empty;
        public string Xbm { get; set; } = string.Empty;
        public string Xmc { get; set; } = string.Empty;
        public string Xggxh { get; set; } = string.Empty;
        public decimal? XbjDj { get; set; }
        public decimal? Xsl { get; set; }
        public DateTime? XbjbDatetime { get; set; }
        public string Xwzdh { get; set; } = string.Empty;
        public string Famc { get; set; } = string.Empty;
        public int Dqzt { get; set; }
    }
}
