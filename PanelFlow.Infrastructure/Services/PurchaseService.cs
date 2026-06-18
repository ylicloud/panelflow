using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Services;

public class PurchaseService : IPurchaseService
{
    private readonly ApplicationDbContext _db;

    public PurchaseService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<PurchasePlanDto>> GetPlanListAsync(
        string? keyword, short? statusFilter, int page, int pageSize, bool issuedOnly = false)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 ? 30 : Math.Min(pageSize, 100);

        var query = _db.PurchasePlans.AsNoTracking();
        if (issuedOnly)
            query = query.Where(p => p.Status >= PurchasePlanStatus.Issued);
        else if (statusFilter.HasValue)
            query = query.Where(p => p.Status == statusFilter.Value);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var q = keyword.Trim();
            query = query.Where(p =>
                p.PlanNo.Contains(q) ||
                p.Fabh.Contains(q) ||
                (p.ContractNo != null && p.ContractNo.Contains(q)));
        }

        var totalCount = await query.CountAsync();
        var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize);
        if (page > totalPages) page = totalPages;

        var plans = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PurchasePlanDto
            {
                Id = p.Id,
                PlanNo = p.PlanNo,
                Fabh = p.Fabh.Trim(),
                ContractNo = p.ContractNo != null ? p.ContractNo.Trim() : null,
                Status = p.Status,
                Creator = p.Creator.Trim(),
                Reviewer = p.Reviewer != null ? p.Reviewer.Trim() : null,
                UnitChief = p.UnitChief != null ? p.UnitChief.Trim() : null,
                CreatedAt = p.CreatedAt,
                IssuedAt = p.IssuedAt,
                IssuedBy = p.IssuedBy != null ? p.IssuedBy.Trim() : null,
                ItemCount = p.Items.Count
            })
            .ToListAsync();

        await FillContractNamesAsync(plans);

        return new PagedResult<PurchasePlanDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Items = plans
        };
    }

    public async Task<PurchasePlanDto?> GetPlanByIdAsync(int planId, bool includeItems = true)
    {
        var plan = await _db.PurchasePlans.AsNoTracking()
            .Where(p => p.Id == planId)
            .Select(p => new PurchasePlanDto
            {
                Id = p.Id,
                PlanNo = p.PlanNo,
                Fabh = p.Fabh.Trim(),
                ContractNo = p.ContractNo != null ? p.ContractNo.Trim() : null,
                Status = p.Status,
                Creator = p.Creator.Trim(),
                Reviewer = p.Reviewer != null ? p.Reviewer.Trim() : null,
                UnitChief = p.UnitChief != null ? p.UnitChief.Trim() : null,
                CreatedAt = p.CreatedAt,
                IssuedAt = p.IssuedAt,
                IssuedBy = p.IssuedBy != null ? p.IssuedBy.Trim() : null,
                ItemCount = p.Items.Count
            })
            .FirstOrDefaultAsync();

        if (plan == null) return null;

        await FillContractNamesAsync([plan]);

        if (includeItems)
        {
            plan.Items = await _db.PurchasePlanItems.AsNoTracking()
                .Where(i => i.PlanId == planId)
                .OrderBy(i => i.SortNo)
                .ThenBy(i => i.Id)
                .Select(i => new PurchasePlanItemDto
                {
                    Id = i.Id,
                    PlanId = i.PlanId,
                    SortNo = i.SortNo,
                    ItemName = i.ItemName.Trim(),
                    ItemSpec = i.ItemSpec.Trim(),
                    ItemUnit = i.ItemUnit != null ? i.ItemUnit.Trim() : null,
                    ItemQty = i.ItemQty,
                    ItemNoBuyQty = i.ItemNoBuyQty,
                    ItemManufacturer = i.ItemManufacturer != null ? i.ItemManufacturer.Trim() : null,
                    ChangeType = i.ChangeType,
                    ChangeRemark = i.ChangeRemark != null ? i.ChangeRemark.Trim() : null,
                    NeedDate = i.NeedDate,
                    Remark = i.Remark != null ? i.Remark.Trim() : null,
                    HasCert = i.HasCert,
                    HasInspection = i.HasInspection,
                    AppearanceOk = i.AppearanceOk,
                    HasAccessories = i.HasAccessories,
                    HasDocuments = i.HasDocuments,
                    VerifyDate = i.VerifyDate,
                    Conclusion = i.Conclusion != null ? i.Conclusion.Trim() : null,
                    Verifier = i.Verifier != null ? i.Verifier.Trim() : null,
                    VerifiedAt = i.VerifiedAt
                })
                .ToListAsync();
        }

        return plan;
    }

    public async Task<bool> HasSummaryDataAsync(string fabh)
    {
        var trimmed = fabh.Trim();
        return await _db.BjbXmyjhzItems.AsNoTracking()
            .AnyAsync(x => x.fabh == trimmed);
    }

    public async Task<(bool Success, string Message, int? PlanId)> CreatePlanFromFabhAsync(
        string fabh, string creator)
    {
        var trimmedFabh = fabh.Trim();
        if (string.IsNullOrEmpty(trimmedFabh))
            return (false, "方案编号不能为空。", null);

        var quotationExists = await _db.BjfatQuotations.AsNoTracking()
            .AnyAsync(q => q.fabh == trimmedFabh);
        if (!quotationExists)
            return (false, "报价单不存在。", null);

        var summaryRows = await _db.BjbXmyjhzItems.AsNoTracking()
            .Where(x => x.fabh == trimmedFabh && x.x_sl > 0)
            .OrderBy(x => x.x_flbh)
            .ThenBy(x => x.x_ggxh)
            .ThenBy(x => x.x_sccj)
            .ToListAsync();

        if (summaryRows.Count == 0)
            return (false, "未找到汇总数据，请先在 PB 中对报价单执行「汇总」操作。", null);

        var contract = await _db.XmylbContracts.AsNoTracking()
            .Where(c => c.bjd_fabh != null && c.bjd_fabh.Trim() == trimmedFabh)
            .OrderByDescending(c => c.qdsj)
            .Select(c => new { c.xmbh, c.xmmc })
            .FirstOrDefaultAsync();

        var planNo = await GeneratePlanNoAsync();

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var plan = new PurchasePlan
            {
                PlanNo = planNo,
                Fabh = trimmedFabh.PadRight(20).Substring(0, 20),
                ContractNo = contract?.xmbh?.Trim(),
                Status = PurchasePlanStatus.Draft,
                Creator = creator.Trim(),
                CreatedAt = DateTime.Now
            };
            _db.PurchasePlans.Add(plan);
            await _db.SaveChangesAsync();

            var sortNo = 1;
            foreach (var row in summaryRows)
            {
                var name = TrimOrEmpty(row.x_mc);
                if (string.IsNullOrEmpty(name))
                    name = TrimOrEmpty(row.x_ggxh);

                _db.PurchasePlanItems.Add(new PurchasePlanItem
                {
                    PlanId = plan.Id,
                    SortNo = sortNo++,
                    ItemName = name,
                    ItemSpec = TrimOrEmpty(row.x_ggxh),
                    ItemUnit = TrimOrNull(row.x_dw),
                    ItemQty = row.x_sl,
                    ItemNoBuyQty = row.x_bcg_sl,
                    ItemManufacturer = TrimOrNull(row.x_sccj),
                    ChangeType = PurchaseChangeType.Normal
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return (true, $"采购计划已创建，共 {summaryRows.Count} 条明细。", plan.Id);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool Success, string Message)> SavePlanItemsAsync(
        int planId, IReadOnlyList<PurchasePlanItemDto> items)
    {
        var plan = await _db.PurchasePlans
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == planId);

        if (plan == null) return (false, "采购计划不存在。");
        if (plan.Status != PurchasePlanStatus.Draft)
            return (false, "仅草稿状态可编辑。");

        var existingIds = plan.Items.Select(i => i.Id).ToHashSet();
        var incomingIds = items.Where(i => i.Id > 0).Select(i => i.Id).ToHashSet();

        foreach (var item in plan.Items.Where(i => !incomingIds.Contains(i.Id)).ToList())
            _db.PurchasePlanItems.Remove(item);

        var sortNo = 1;
        foreach (var dto in items.OrderBy(i => i.SortNo).ThenBy(i => i.Id))
        {
            if (dto.Id > 0 && existingIds.Contains(dto.Id))
            {
                var entity = plan.Items.First(i => i.Id == dto.Id);
                ApplyItemDto(entity, dto, sortNo++);
            }
            else if (dto.Id <= 0)
            {
                var entity = new PurchasePlanItem { PlanId = planId };
                ApplyItemDto(entity, dto, sortNo++);
                if (entity.ChangeType == PurchaseChangeType.Normal)
                    entity.ChangeType = PurchaseChangeType.Added;
                _db.PurchasePlanItems.Add(entity);
            }
        }

        await _db.SaveChangesAsync();
        return (true, "保存成功。");
    }

    public async Task<(bool Success, string Message)> IssuePlanAsync(int planId, string issuedBy)
    {
        var plan = await _db.PurchasePlans
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == planId);

        if (plan == null) return (false, "采购计划不存在。");
        if (plan.Status != PurchasePlanStatus.Draft)
            return (false, "仅草稿状态可下达。");
        if (plan.Items.Count == 0)
            return (false, "计划明细为空，无法下达。");

        plan.Status = PurchasePlanStatus.Issued;
        plan.IssuedAt = DateTime.Now;
        plan.IssuedBy = issuedBy.Trim();
        await _db.SaveChangesAsync();
        return (true, "采购计划已下达，采购人员可在「采购管理」中查看。");
    }

    public async Task<(bool Success, string Message)> SaveVerificationAsync(
        int planId, IReadOnlyList<PurchasePlanItemDto> items)
    {
        var plan = await _db.PurchasePlans
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == planId);

        if (plan == null) return (false, "采购计划不存在。");
        if (plan.Status < PurchasePlanStatus.Issued)
            return (false, "计划尚未下达。");

        foreach (var dto in items)
        {
            var entity = plan.Items.FirstOrDefault(i => i.Id == dto.Id);
            if (entity == null) continue;

            entity.HasCert = dto.HasCert;
            entity.HasInspection = dto.HasInspection;
            entity.AppearanceOk = dto.AppearanceOk;
            entity.HasAccessories = dto.HasAccessories;
            entity.HasDocuments = dto.HasDocuments;
            entity.VerifyDate = dto.VerifyDate;
            entity.Conclusion = dto.Conclusion?.Trim();
            entity.Verifier = dto.Verifier?.Trim();
            entity.VerifiedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return (true, "验证记录已保存。");
    }

    public async Task<PurchaseReport1DataDto?> GetReport1DataAsync(int planId, bool showDeleted)
    {
        var plan = await GetPlanByIdAsync(planId, includeItems: true);
        if (plan == null) return null;

        var rows = plan.Items
            .Where(i => showDeleted || i.ChangeType != PurchaseChangeType.Deleted)
            .OrderBy(i => i.SortNo)
            .Select((item, index) => new PurchaseReport1RowDto
            {
                Seq = index + 1,
                ItemName = item.ItemName.Trim(),
                ItemSpec = item.ItemSpec.Trim(),
                ItemUnit = item.ItemUnit?.Trim() ?? string.Empty,
                ItemQty = item.ItemQty,
                NeedDateText = item.NeedDate?.ToString("yyyy-MM-dd"),
                ItemManufacturer = item.ItemManufacturer?.Trim() ?? string.Empty,
                Remark = item.DisplayRemark,
                IsDeleted = item.ChangeType == PurchaseChangeType.Deleted
            })
            .ToList();

        return new PurchaseReport1DataDto
        {
            PlanNo = plan.PlanNo,
            ContractName = plan.ContractName ?? string.Empty,
            ContractNo = plan.ContractNo ?? string.Empty,
            Fabh = plan.Fabh,
            Rows = rows
        };
    }

    public async Task<PurchaseReport2DataDto?> GetReport2DataAsync(int planId, bool showDeleted)
    {
        var plan = await GetPlanByIdAsync(planId, includeItems: true);
        if (plan == null) return null;

        var rows = plan.Items
            .Where(i => showDeleted || i.ChangeType != PurchaseChangeType.Deleted)
            .OrderBy(i => i.SortNo)
            .Select((item, index) => new PurchaseReport2RowDto
            {
                Seq = index + 1,
                ContractNo = plan.ContractNo ?? string.Empty,
                ItemName = item.ItemName.Trim(),
                ItemSpec = item.ItemSpec.Trim(),
                ItemQty = item.ItemQty,
                ItemManufacturer = item.ItemManufacturer?.Trim() ?? string.Empty,
                HasCertText = BoolText(item.HasCert),
                HasInspectionText = BoolText(item.HasInspection),
                AppearanceText = BoolText(item.AppearanceOk),
                HasAccessoriesText = BoolText(item.HasAccessories),
                HasDocumentsText = BoolText(item.HasDocuments),
                VerifyDateText = item.VerifyDate?.ToString("yyyy-MM-dd"),
                Conclusion = item.Conclusion?.Trim() ?? string.Empty,
                Verifier = item.Verifier?.Trim() ?? string.Empty,
                IsDeleted = item.ChangeType == PurchaseChangeType.Deleted
            })
            .ToList();

        return new PurchaseReport2DataDto
        {
            PlanNo = plan.PlanNo,
            Rows = rows
        };
    }

    private async Task FillContractNamesAsync(List<PurchasePlanDto> plans)
    {
        var contractNos = plans
            .Where(p => !string.IsNullOrWhiteSpace(p.ContractNo))
            .Select(p => p.ContractNo!.Trim())
            .Distinct()
            .ToList();

        if (contractNos.Count == 0) return;

        var nameDict = await _db.XmylbContracts.AsNoTracking()
            .Where(c => contractNos.Contains(c.xmbh))
            .Select(c => new { c.xmbh, c.xmmc })
            .ToDictionaryAsync(c => c.xmbh.Trim(), c => c.xmmc.Trim());

        foreach (var plan in plans)
        {
            if (plan.ContractNo != null && nameDict.TryGetValue(plan.ContractNo.Trim(), out var name))
                plan.ContractName = name;
        }
    }

    private async Task<string> GeneratePlanNoAsync()
    {
        var year = DateTime.Now.Year;
        var prefix = $"PC{year}";
        var lastNo = await _db.PurchasePlans.AsNoTracking()
            .Where(p => p.PlanNo.StartsWith(prefix))
            .OrderByDescending(p => p.PlanNo)
            .Select(p => p.PlanNo)
            .FirstOrDefaultAsync();

        var seq = 1;
        if (!string.IsNullOrEmpty(lastNo) && lastNo.Length > prefix.Length)
        {
            if (int.TryParse(lastNo[prefix.Length..], out var lastSeq))
                seq = lastSeq + 1;
        }

        return $"{prefix}{seq:D4}";
    }

    private static void ApplyItemDto(PurchasePlanItem entity, PurchasePlanItemDto dto, int sortNo)
    {
        entity.SortNo = sortNo;
        entity.ItemName = dto.ItemName.Trim();
        entity.ItemSpec = dto.ItemSpec.Trim();
        entity.ItemUnit = TrimOrNull(dto.ItemUnit);
        entity.ItemQty = dto.ItemQty;
        entity.ItemNoBuyQty = dto.ItemNoBuyQty;
        entity.ItemManufacturer = TrimOrNull(dto.ItemManufacturer);
        entity.ChangeType = dto.ChangeType;
        entity.ChangeRemark = TrimOrNull(dto.ChangeRemark);
        entity.NeedDate = dto.NeedDate;
        entity.Remark = TrimOrNull(dto.Remark);
    }

    private static PurchasePlanItemDto ToItemDto(PurchasePlanItem i) => new()
    {
        Id = i.Id,
        PlanId = i.PlanId,
        SortNo = i.SortNo,
        ItemName = i.ItemName.Trim(),
        ItemSpec = i.ItemSpec.Trim(),
        ItemUnit = TrimOrNull(i.ItemUnit),
        ItemQty = i.ItemQty,
        ItemNoBuyQty = i.ItemNoBuyQty,
        ItemManufacturer = TrimOrNull(i.ItemManufacturer),
        ChangeType = i.ChangeType,
        ChangeRemark = TrimOrNull(i.ChangeRemark),
        NeedDate = i.NeedDate,
        Remark = TrimOrNull(i.Remark),
        HasCert = i.HasCert,
        HasInspection = i.HasInspection,
        AppearanceOk = i.AppearanceOk,
        HasAccessories = i.HasAccessories,
        HasDocuments = i.HasDocuments,
        VerifyDate = i.VerifyDate,
        Conclusion = TrimOrNull(i.Conclusion),
        Verifier = TrimOrNull(i.Verifier),
        VerifiedAt = i.VerifiedAt
    };

    private static string TrimOrEmpty(string? value) => value?.Trim() ?? string.Empty;

    private static string? TrimOrNull(string? value)
    {
        var t = value?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private static string BoolText(bool? value) => value switch
    {
        true => "有",
        false => "无",
        _ => string.Empty
    };
}
