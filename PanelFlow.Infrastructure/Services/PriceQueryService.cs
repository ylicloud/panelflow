using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Core.Utilities;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Services;

public class PriceQueryService : IPriceQueryService
{
    public const int MaxBatchSize = 100;
    private const string NotFoundMessage = "历史价格表中无此型号指纹";

    private readonly ApplicationDbContext _db;

    public PriceQueryService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PriceQueryResultDto> QueryBySpecAsync(string? spec, string? wzdh)
    {
        var trimmedWzdh = (wzdh ?? string.Empty).Trim();
        var trimmedSpec = (spec ?? string.Empty).Trim();
        var effectiveWzdh = !string.IsNullOrEmpty(trimmedWzdh)
            ? trimmedWzdh
            : SpecNormalizer.Normalize(trimmedSpec);

        if (string.IsNullOrEmpty(effectiveWzdh))
        {
            return new PriceQueryResultDto
            {
                Found = false,
                InputSpec = string.IsNullOrEmpty(trimmedSpec) ? null : trimmedSpec,
                XWzdh = string.Empty,
                Message = "型号或指纹不能为空"
            };
        }

        var row = await _db.StdPriceHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.x_wzdh == effectiveWzdh);

        return MapResult(trimmedSpec, effectiveWzdh, row);
    }

    public async Task<PriceBatchQueryResultDto> QueryBatchAsync(PriceBatchQueryRequest request)
    {
        var entries = BuildBatchEntries(request);
        if (entries.Count == 0)
        {
            return new PriceBatchQueryResultDto();
        }

        if (entries.Count > MaxBatchSize)
        {
            throw new ArgumentException($"批量查询最多 {MaxBatchSize} 条");
        }

        var wzdhSet = entries.Select(e => e.Wzdh).Distinct(StringComparer.Ordinal).ToList();
        var rows = await _db.StdPriceHistories
            .AsNoTracking()
            .Where(x => wzdhSet.Contains(x.x_wzdh))
            .ToListAsync();

        var rowMap = rows.ToDictionary(x => x.x_wzdh, StringComparer.Ordinal);
        var items = entries
            .Select(e => MapResult(e.InputSpec, e.Wzdh, rowMap.GetValueOrDefault(e.Wzdh)))
            .ToList();

        return new PriceBatchQueryResultDto
        {
            Total = items.Count,
            FoundCount = items.Count(x => x.Found),
            Items = items
        };
    }

    private static List<BatchEntry> BuildBatchEntries(PriceBatchQueryRequest request)
    {
        var entries = new List<BatchEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in request.WzdhList ?? [])
        {
            var wzdh = (raw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(wzdh) || !seen.Add(wzdh))
                continue;

            entries.Add(new BatchEntry(null, wzdh));
        }

        foreach (var raw in request.Specs ?? [])
        {
            var spec = (raw ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(spec))
                continue;

            var wzdh = SpecNormalizer.Normalize(spec);
            if (string.IsNullOrEmpty(wzdh) || !seen.Add(wzdh))
                continue;

            entries.Add(new BatchEntry(spec, wzdh));
        }

        return entries;
    }

    private static PriceQueryResultDto MapResult(string? inputSpec, string wzdh, StdPriceHistory? row)
    {
        if (row == null)
        {
            return new PriceQueryResultDto
            {
                Found = false,
                InputSpec = string.IsNullOrEmpty(inputSpec) ? null : inputSpec,
                XWzdh = wzdh,
                Message = NotFoundMessage
            };
        }

        return new PriceQueryResultDto
        {
            Found = true,
            InputSpec = string.IsNullOrEmpty(inputSpec) ? row.ggxh : inputSpec,
            XWzdh = row.x_wzdh,
            Ggxh = row.ggxh,
            XMc = row.x_mc,
            XDw = row.x_dw,
            XSccj = row.x_sccj,
            LastPrice = row.last_price,
            LastFabh = row.last_fabh,
            LastDate = row.last_date,
            AvgPrice = row.avg_price,
            AvgCount = row.avg_count,
            MinPrice = row.min_price,
            MaxPrice = row.max_price
        };
    }

    private sealed record BatchEntry(string? InputSpec, string Wzdh);
}
