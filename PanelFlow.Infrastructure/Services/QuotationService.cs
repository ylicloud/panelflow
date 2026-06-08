using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Services;

public class QuotationService : IQuotationService
{
    private readonly ApplicationDbContext _db;

    public QuotationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<QuotationDto>> GetListAsync(string? keyword, bool includeHistory, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 ? 30 : pageSize;
        if (pageSize > 100) pageSize = 100;

        var query =
            from q in _db.BjfatQuotations.AsNoTracking()
            join c0 in _db.KhylbCustomers.AsNoTracking()
                on q.khbh.Trim() equals c0.gsbh into cg
            from c in cg.DefaultIfEmpty()
            select new { q, c };

        if (!includeHistory)
        {
            var cutoff = DateTime.Today.AddYears(-2);
            query = query.Where(x => x.q.fasj.HasValue && x.q.fasj.Value >= cutoff);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(x =>
                x.q.fabh.Contains(kw) ||
                x.q.famc.Contains(kw) ||
                x.q.bjr.Contains(kw) ||
                x.q.khbh.Contains(kw) ||
                (x.c != null && x.c.gsmc.Contains(kw)) ||
                (x.c != null && x.c.gsld.Contains(kw)));
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        if (totalPages == 0) totalPages = 1;
        if (page > totalPages) page = totalPages;

        var items = await query
            .OrderByDescending(x => x.q.fasj)
            .ThenByDescending(x => x.q.fabh)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new QuotationDto
            {
                QuotationNo = (x.q.fabh ?? string.Empty).Trim(),
                CreatedAt = x.q.fasj,
                QuotationName = (x.q.famc ?? string.Empty).Trim(),
                PlanModelNo = x.q.famxbh,
                Quoter = (x.q.bjr ?? string.Empty).Trim(),
                Remark = (x.q.bz ?? string.Empty).Trim(),
                CustomerNo = (x.q.khbh ?? string.Empty).Trim(),
                CustomerName = BuildCustomerName(x.c),
                PlanType = x.q.falx,
                CurrentStatus = x.q.dqzt
            })
            .ToListAsync();

        return new PagedResult<QuotationDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Items = items
        };
    }

    public async Task<QuotationDto?> GetByQuotationNoAsync(string quotationNo)
    {
        if (string.IsNullOrWhiteSpace(quotationNo))
            return null;

        var key = quotationNo.Trim();
        var data = await (
            from q in _db.BjfatQuotations.AsNoTracking()
            join c0 in _db.KhylbCustomers.AsNoTracking()
                on q.khbh.Trim() equals c0.gsbh into cg
            from c in cg.DefaultIfEmpty()
            where q.fabh == key
            select new { q, c }
        ).FirstOrDefaultAsync();

        if (data == null)
            return null;

        return new QuotationDto
        {
            QuotationNo = (data.q.fabh ?? string.Empty).Trim(),
            CreatedAt = data.q.fasj,
            QuotationName = (data.q.famc ?? string.Empty).Trim(),
            PlanModelNo = data.q.famxbh,
            Quoter = (data.q.bjr ?? string.Empty).Trim(),
            Remark = (data.q.bz ?? string.Empty).Trim(),
            CustomerNo = (data.q.khbh ?? string.Empty).Trim(),
            CustomerName = BuildCustomerName(data.c),
            PlanType = data.q.falx,
            CurrentStatus = data.q.dqzt
        };
    }

    public async Task<List<QuotationDto>> GetByCustomerNoAsync(string customerNo)
    {
        if (string.IsNullOrWhiteSpace(customerNo))
            return [];

        var key = customerNo.Trim();
        return await (
            from q in _db.BjfatQuotations.AsNoTracking()
            join c0 in _db.KhylbCustomers.AsNoTracking()
                on q.khbh.Trim() equals c0.gsbh into cg
            from c in cg.DefaultIfEmpty()
            where q.khbh.Trim() == key
            orderby q.fasj descending, q.fabh descending
            select new QuotationDto
            {
                QuotationNo = (q.fabh ?? string.Empty).Trim(),
                CreatedAt = q.fasj,
                QuotationName = (q.famc ?? string.Empty).Trim(),
                PlanModelNo = q.famxbh,
                Quoter = (q.bjr ?? string.Empty).Trim(),
                Remark = (q.bz ?? string.Empty).Trim(),
                CustomerNo = (q.khbh ?? string.Empty).Trim(),
                CustomerName = BuildCustomerName(c),
                PlanType = q.falx,
                CurrentStatus = q.dqzt
            }
        ).ToListAsync();
    }

    public async Task<(bool Success, string Message)> CreateAsync(QuotationDto dto)
    {
        var quotationNo = Safe(dto.QuotationNo, 20);
        var quotationName = Safe(dto.QuotationName, 50);
        var customerNo = Safe(dto.CustomerNo, 10);
        var quoter = Safe(dto.Quoter, 10);
        if (string.IsNullOrWhiteSpace(quotationNo))
            return (false, "报价单编号不能为空");
        if (string.IsNullOrWhiteSpace(quotationName))
            return (false, "报价单名称不能为空");
        if (string.IsNullOrWhiteSpace(customerNo))
            return (false, "客户编号不能为空");
        if (string.IsNullOrWhiteSpace(quoter))
            return (false, "报价人不能为空");

        var exists = await _db.BjfatQuotations.AnyAsync(x => x.fabh == quotationNo);
        if (exists)
            return (false, $"报价单编号 \"{quotationNo}\" 已存在");

        // 1) 先判断 BJB 中不能存在同 fabh 数据
        var bjbExists = await _db.BjbItems.AsNoTracking().AnyAsync(x => x.fabh == quotationNo);
        if (bjbExists)
            return (false, $"BJB 中已存在方案编号 \"{quotationNo}\"，不能重复创建");

        var entity = new BjfatQuotation
        {
            fabh = quotationNo,
            fasj = dto.CreatedAt ?? DateTime.Now,
            famc = quotationName,
            famxbh = 0,
            bjr = quoter,
            bz = Safe(dto.Remark, 50),
            khbh = customerNo,
            falx = 1,
            dqzt = 1
        };

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // 2) 插入 BJFAT
            _db.BjfatQuotations.Add(entity);
            await _db.SaveChangesAsync();

            // 3) 同时插入 BJB 两条默认行
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO BJB
(fabh, x_bm, x_mc, x_dw, x_dj, x_fdds, x_sl, x_bj_fdds, x_bj_dj, x_bjb_bj, x_bjb_dj,
 x_bjb_datetime, x_bjb_fdds, x_wzfy, x_flbh, x_ggxh, x_sccj, x_key_ry, x_jsgsbh, x_bz, x_wzdh, x_lx, x_cgf)
VALUES
({quotationNo}, {"0"}, {string.Empty}, {string.Empty}, {0m}, {0m}, {0m}, {0m}, {0m}, {0m}, {0m},
 NULL, {0m}, {0m}, {string.Empty}, {string.Empty}, {string.Empty}, {string.Empty}, {0m}, {string.Empty}, {string.Empty}, {0}, {0})");

            await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO BJB
(fabh, x_bm, x_mc, x_dw, x_dj, x_fdds, x_sl, x_bj_fdds, x_bj_dj, x_bjb_bj, x_bjb_dj,
 x_bjb_datetime, x_bjb_fdds, x_wzfy, x_flbh, x_ggxh, x_sccj, x_key_ry, x_jsgsbh, x_bz, x_wzdh, x_lx, x_cgf)
VALUES
({quotationNo}, {"9999"}, {"总计"}, {string.Empty}, {0m}, {0m}, {0m}, {0m}, {0m}, {0m}, {0m},
 NULL, {0m}, {0m}, {string.Empty}, {string.Empty}, {string.Empty}, {string.Empty}, {0m}, {string.Empty}, {string.Empty}, {0}, {0})");

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return (true, "报价单创建成功");
    }

    public async Task<(bool Success, string Message)> UpdateAsync(QuotationDto dto, string operatorUserName)
    {
        var quotationNo = Safe(dto.QuotationNo, 20);
        if (string.IsNullOrWhiteSpace(quotationNo))
            return (false, "报价单编号不能为空");

        var entity = await FindQuotationEntityAsync(quotationNo);
        if (entity == null)
            return (false, "报价单不存在");

        var (allowed, accessMessage) = ValidateEditAccess(entity, operatorUserName);
        if (!allowed)
            return (false, accessMessage);

        entity.fasj = dto.CreatedAt ?? entity.fasj;
        entity.famc = Safe(dto.QuotationName, 50);
        entity.famxbh = dto.PlanModelNo;
        entity.bjr = Safe(dto.Quoter, 10);
        entity.bz = Safe(dto.Remark, 50);
        entity.khbh = Safe(dto.CustomerNo, 10);
        entity.falx = dto.PlanType;
        entity.dqzt = dto.CurrentStatus;

        _db.BjfatQuotations.Update(entity);
        await _db.SaveChangesAsync();
        return (true, "报价单信息已更新");
    }

    public async Task<(bool Allowed, string Message)> ValidateEditAccessAsync(string quotationNo, string operatorUserName)
    {
        var key = Safe(quotationNo, 20);
        if (string.IsNullOrWhiteSpace(key))
            return (false, "报价单编号不能为空");

        var entity = await FindQuotationEntityAsync(key);
        if (entity == null)
            return (false, "报价单不存在");

        return ValidateEditAccess(entity, operatorUserName);
    }

    public async Task<(bool CanRename, string Message)> CanRenameFabhAsync(string originalFabh, string operatorUserName)
    {
        var key = Safe(originalFabh, 20);
        if (string.IsNullOrWhiteSpace(key))
            return (false, "报价单编号不能为空");

        var entity = await FindQuotationEntityAsync(key);
        if (entity == null)
            return (false, "报价单不存在");

        return await ValidateRenamePreconditionsAsync(entity, operatorUserName);
    }

    public async Task<RenameFabhResult> RenameFabhAsync(string originalFabh, string newFabh, string operatorUserName)
    {
        var oldFabh = Safe(originalFabh, 20);
        var newFabhSafe = Safe(newFabh, 20);
        if (string.IsNullOrWhiteSpace(oldFabh))
            return FailRename("报价单编号不能为空");
        if (string.IsNullOrWhiteSpace(newFabhSafe))
            return FailRename("新方案编号不能为空");
        if (string.Equals(oldFabh, newFabhSafe, StringComparison.OrdinalIgnoreCase))
            return FailRename("新方案编号与原编号相同");

        var entity = await FindQuotationEntityAsync(oldFabh);
        if (entity == null)
            return FailRename("报价单不存在");

        var (canRename, message) = await ValidateRenamePreconditionsAsync(entity, operatorUserName);
        if (!canRename)
            return FailRename(message);

        var exists = await _db.BjfatQuotations.AsNoTracking()
            .AnyAsync(x => x.fabh == newFabhSafe);
        if (exists)
            return FailRename($"方案编号 \"{newFabhSafe}\" 已存在，不能修改为该编号");

        var affected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var previousTimeout = _db.Database.GetCommandTimeout();
        // 改号需 UPDATE 多张百万级历史表，默认 30 秒易超时
        _db.Database.SetCommandTimeout(600);

        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                affected["BJB"] = await ExecuteFabhUpdateAsync("BJB", "fabh", oldFabh, newFabhSafe);
                affected["BJB_HZB"] = await ExecuteFabhUpdateAsync("BJB_HZB", "FABH", oldFabh, newFabhSafe);
                affected["BJB_XMYJB"] = await ExecuteFabhUpdateAsync("BJB_XMYJB", "fabh", oldFabh, newFabhSafe);
                affected["BJB_XMYJHZ"] = await ExecuteFabhUpdateAsync("BJB_XMYJHZ", "fabh", oldFabh, newFabhSafe);
                affected["BJB_XMHZ"] = await ExecuteFabhUpdateAsync("BJB_XMHZ", "fabh", oldFabh, newFabhSafe);
                affected["XM_YJB"] = await ExecuteFabhUpdateAsync("XM_YJB", "fabh", oldFabh, newFabhSafe);
                affected["XM_CKJS"] = await ExecuteFabhUpdateAsync("XM_CKJS", "fabh", oldFabh, newFabhSafe);
                affected["XM_YJHZ"] = await ExecuteFabhUpdateAsync("XM_YJHZ", "fabh", oldFabh, newFabhSafe);
                affected["XM_HZ"] = await ExecuteFabhUpdateAsync("XM_HZ", "fabh", oldFabh, newFabhSafe);
                affected["BJFAT"] = await ExecuteFabhUpdateAsync("BJFAT", "fabh", oldFabh, newFabhSafe);

                if (affected["BJFAT"] != 1)
                {
                    await tx.RollbackAsync();
                    return FailRename("更新报价单主表失败，请稍后重试");
                }

                await tx.CommitAsync();
            }
            catch (SqlException ex) when (ex.Number == -2)
            {
                await tx.RollbackAsync();
                return FailRename("改号操作超时（数据量较大），请稍后重试或联系管理员");
            }
            catch
            {
                throw;
            }
        }
        finally
        {
            _db.Database.SetCommandTimeout(previousTimeout);
        }

        return new RenameFabhResult
        {
            Success = true,
            Message = "方案编号已修改",
            NewFabh = newFabhSafe,
            AffectedRows = affected
        };
    }

    public async Task<(bool Success, string Message)> DeleteAsync(string quotationNo, string operatorUserName)
    {
        var key = Safe(quotationNo, 20);
        var loginUser = Safe(operatorUserName, 10);
        if (string.IsNullOrWhiteSpace(key))
            return (false, "报价单编号不能为空");
        if (string.IsNullOrWhiteSpace(loginUser))
            return (false, "当前登录用户不能为空");

        var entity = await _db.BjfatQuotations.FirstOrDefaultAsync(x => x.fabh == key);
        if (entity == null)
            return (false, "报价单不存在");

        var owner = (entity.bjr ?? string.Empty).Trim();
        if (!string.Equals(owner, loginUser, StringComparison.OrdinalIgnoreCase))
            return (false, "仅报价人本人可删除该报价单");
        // 与列表 CanShowRowActions 一致：dqzt=10（已成立）不可删，其余状态由报价人删除
        if (entity.dqzt == 10)
            return (false, "已成立（dqzt=10）的报价单不允许删除");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            // 按需求顺序：先删 BJFAT，再删 BJB
            _db.BjfatQuotations.Remove(entity);
            await _db.SaveChangesAsync();

            await _db.Database.ExecuteSqlInterpolatedAsync($@"
DELETE FROM BJB
WHERE fabh = {key}");

            await tx.CommitAsync();
            return (true, "报价单已删除");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<BjfatQuotation?> FindQuotationEntityAsync(string quotationNo)
    {
        var key = Safe(quotationNo, 20);
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return await _db.BjfatQuotations
            .FirstOrDefaultAsync(x => x.fabh == key);
    }

    private static (bool Allowed, string Message) ValidateEditAccess(BjfatQuotation entity, string operatorUserName)
    {
        var loginUser = Safe(operatorUserName, 10);
        if (string.IsNullOrWhiteSpace(loginUser))
            return (false, "当前登录用户不能为空");

        var owner = (entity.bjr ?? string.Empty).Trim();
        if (!string.Equals(owner, loginUser, StringComparison.OrdinalIgnoreCase))
            return (false, "仅报价人本人可编辑该报价单");

        if (entity.dqzt == 10)
            return (false, "已成立（dqzt=10）的报价单不允许编辑");

        return (true, string.Empty);
    }

    private async Task<(bool CanRename, string Message)> ValidateRenamePreconditionsAsync(
        BjfatQuotation entity,
        string operatorUserName)
    {
        var (allowed, accessMessage) = ValidateEditAccess(entity, operatorUserName);
        if (!allowed)
            return (false, accessMessage);

        var oldFabh = Safe(entity.fabh, 20);
        var linkedContract = await _db.XmylbContracts.AsNoTracking()
            .AnyAsync(x => x.bjd_fabh == oldFabh);
        if (linkedContract)
            return (false, "该报价单已关联合同，无法修改方案编号");

        if (!entity.fasj.HasValue)
            return (false, "报价单创建时间为空，无法修改方案编号");

        var days = (DateTime.Today - entity.fasj.Value.Date).TotalDays;
        if (days > 30)
            return (false, "报价单创建已超过 30 天，无法修改方案编号");

        return (true, string.Empty);
    }

    private async Task<int> ExecuteFabhUpdateAsync(string tableName, string columnName, string oldFabh, string newFabh)
    {
        // 与 DeleteAsync 一致：char(20) 等值匹配（勿对列做 LTRIM/RTRIM，否则百万级表全表扫描超时）
        var sql = FormattableString.Invariant(
            $"UPDATE {tableName} SET {columnName} = {{0}} WHERE {columnName} = {{1}}");
        return await _db.Database.ExecuteSqlRawAsync(sql, newFabh, oldFabh);
    }

    private static RenameFabhResult FailRename(string message) =>
        new() { Success = false, Message = message };

    private static string Safe(string? value, int maxLen)
    {
        var v = (value ?? string.Empty).Trim();
        return v.Length <= maxLen ? v : v[..maxLen];
    }

    private static string BuildCustomerName(KhylbCustomer? c)
    {
        if (c == null) return string.Empty;

        var name = (c.gsmc ?? string.Empty).Trim();
        var alias = (c.gsld ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(alias)) return string.Empty;
        if (string.IsNullOrWhiteSpace(alias)) return name;
        if (string.IsNullOrWhiteSpace(name)) return alias;
        return $"{name}({alias})";
    }
}
