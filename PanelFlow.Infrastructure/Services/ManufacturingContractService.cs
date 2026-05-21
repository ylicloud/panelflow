using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Services;

public class ManufacturingContractService : IManufacturingContractService
{
    private readonly ApplicationDbContext _db;

    public ManufacturingContractService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ManufacturingContractDto>> GetListAsync(
        string? keyword, bool includeHistory, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 ? 30 : pageSize;
        if (pageSize > 100) pageSize = 100;

        var query = _db.XmylbContracts.AsNoTracking();
        if (!includeHistory)
        {
            var cutoff = DateTime.Today.AddYears(-3);
            query = query.Where(x => x.qdsj.HasValue && x.qdsj.Value >= cutoff);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var q = keyword.Trim();
            query = query.Where(x => x.xmbh.Contains(q) || x.xmmc.Contains(q) || x.htnr.Contains(q));
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        if (totalPages == 0) totalPages = 1;
        if (page > totalPages) page = totalPages;

        var items = await query
            .OrderByDescending(x => x.qdsj)
            .ThenByDescending(x => x.xmbh)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync();

        // 获取报价人信息：关联BJFAT表
        var planNos = items
            .Where(i => !string.IsNullOrWhiteSpace(i.QuotationPlanNo))
            .Select(i => i.QuotationPlanNo.Trim())
            .Distinct()
            .ToList();

        if (planNos.Count > 0)
        {
            var quoterDict = await _db.BjfatQuotations
                .AsNoTracking()
                .Where(b => planNos.Contains(b.fabh))
                .Select(b => new { b.fabh, b.bjr })
                .ToDictionaryAsync(b => b.fabh.Trim(), b => b.bjr.Trim());

            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.QuotationPlanNo) &&
                    quoterDict.TryGetValue(item.QuotationPlanNo.Trim(), out var quoter))
                {
                    item.Quoter = quoter;
                }
            }
        }

        return new PagedResult<ManufacturingContractDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Items = items
        };
    }

    public async Task<ManufacturingContractDto?> GetByContractNoAsync(string contractNo)
    {
        var key = contractNo.Trim();
        var entity = await _db.XmylbContracts.FirstOrDefaultAsync(x => x.xmbh == key);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<(bool Success, string Message)> CreateAsync(ManufacturingContractDto dto)
    {
        var key = dto.ContractNo.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return (false, "合同编号不能为空");

        var exists = await _db.XmylbContracts.AnyAsync(x => x.xmbh == key);
        if (exists)
            return (false, $"合同编号 \"{key}\" 已存在");

        var entity = new XmylbContract
        {
            xmbh = Safe(key, 20),
            xmmc = Safe(dto.ProjectName, 50),
            xmdh = string.Empty,
            hth_1 = Safe(dto.LegacyContractNo, 10),
            qdsj = dto.SignDate,
            fzr = Safe(dto.Owner, 10),
            htlx = 0,
            htnr = Safe(dto.ContractContent, 100),
            jhsj = dto.DeliveryDate,
            zhtje = dto.TotalAmount,
            dkjl = string.Empty,
            ht_zdk = 0m,
            ht_bfcb = 0m,
            wgcz = 0m,
            sffh = string.Empty,
            sfkfp = string.Empty,
            khbh = Safe(dto.CustomerNo, 10),
            qydw = Safe(dto.SignCompany, 10),
            bjd_fabh = Safe(dto.QuotationPlanNo, 20),
            dqzt = dto.CurrentStatus,
            xgcs = string.Empty,
            beizhu = Safe(dto.Remark, 200),
            htqj_1 = 0,
            htqj_2 = 0,
            htqj_3 = 0,
            htqj_4 = 0,
            htqj_5 = 0,
            htqj_6 = 0,
            xmlx = 0
        };

        _db.XmylbContracts.Add(entity);
        await _db.SaveChangesAsync();
        return (true, "合同创建成功");
    }

    public async Task<(bool Success, string Message)> UpdateAsync(ManufacturingContractDto dto)
    {
        var key = dto.ContractNo.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return (false, "合同编号不能为空");

        var entity = await _db.XmylbContracts.FirstOrDefaultAsync(x => x.xmbh == key);
        if (entity == null)
            return (false, "合同不存在");

        entity.xmmc = Safe(dto.ProjectName, 50);
        entity.qdsj = dto.SignDate;
        entity.fzr = Safe(dto.Owner, 10);
        entity.htnr = Safe(dto.ContractContent, 100);
        entity.jhsj = dto.DeliveryDate;
        entity.zhtje = dto.TotalAmount;
        entity.khbh = Safe(dto.CustomerNo, 10);
        entity.qydw = Safe(dto.SignCompany, 10);
        entity.dqzt = dto.CurrentStatus;
        entity.beizhu = Safe(dto.Remark, 200);

        _db.XmylbContracts.Update(entity);
        await _db.SaveChangesAsync();
        return (true, "合同信息已更新");
    }

    public async Task<(bool Success, string Message)> DeleteAsync(string contractNo)
    {
        var key = contractNo.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return (false, "合同编号不能为空");

        var entity = await _db.XmylbContracts.FirstOrDefaultAsync(x => x.xmbh == key);
        if (entity == null)
            return (false, "合同不存在");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var quotePlanNo = entity.bjd_fabh?.Trim();
            if (!string.IsNullOrWhiteSpace(quotePlanNo))
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE BJFAT
SET dqzt = 0
WHERE fabh = {quotePlanNo}");
            }

            _db.XmylbContracts.Remove(entity);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return (true, "合同已删除");
        }
        catch (Exception)
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static ManufacturingContractDto ToDto(XmylbContract x)
    {
        return new ManufacturingContractDto
        {
            ContractNo = SafeTrim(x.xmbh),
            ProjectName = SafeTrim(x.xmmc),
            LegacyContractNo = SafeTrim(x.hth_1),
            SignDate = x.qdsj,
            Owner = SafeTrim(x.fzr),
            ContractContent = SafeTrim(x.htnr),
            DeliveryDate = x.jhsj,
            TotalAmount = x.zhtje,
            CustomerNo = SafeTrim(x.khbh),
            SignCompany = SafeTrim(x.qydw),
            QuotationPlanNo = SafeTrim(x.bjd_fabh),
            CurrentStatus = x.dqzt,
            Remark = SafeTrim(x.beizhu)
        };
    }

    private static string Safe(string? value, int maxLen)
    {
        var v = (value ?? string.Empty).Trim();
        return v.Length <= maxLen ? v : v[..maxLen];
    }

    private static string SafeTrim(string? value) => value?.Trim() ?? string.Empty;
}
