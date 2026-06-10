using Microsoft.EntityFrameworkCore;
using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;
using System.Text.Json;

namespace PanelFlow.Infrastructure.Services;

public class ElementDictService : IElementDictService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditLogService _auditLogService;

    public ElementDictService(ApplicationDbContext db, IAuditLogService auditLogService)
    {
        _db = db;
        _auditLogService = auditLogService;
    }

    public async Task<IReadOnlyList<ElementDictDto>> GetByLevelAsync(byte level, bool includeDisabled)
    {
        var query = _db.StdElementDicts.AsNoTracking().Where(x => x.Level == level);
        if (!includeDisabled)
        {
            query = query.Where(x => x.IsEnabled);
        }

        var items = await query
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync();

        return items.Select(ToDto).ToList();
    }

    public async Task<int> CreateAsync(ElementDictDto dto, string userName)
    {
        var maxSort = await _db.StdElementDicts
            .Where(x => x.Level == dto.Level)
            .Select(x => (int?)x.SortOrder)
            .MaxAsync() ?? 0;

        var entity = new StdElementDict
        {
            Level = dto.Level,
            Name = (dto.Name ?? string.Empty).Trim(),
            Xlx = dto.Xlx,
            Amount = dto.Amount > 0 ? dto.Amount : 1m,
            Ggxh = string.IsNullOrWhiteSpace(dto.Ggxh) ? null : dto.Ggxh.Trim(),
            DefaultUnit = string.IsNullOrWhiteSpace(dto.DefaultUnit) ? null : dto.DefaultUnit.Trim(),
            TargetParentScope = string.IsNullOrWhiteSpace(dto.TargetParentScope) ? null : dto.TargetParentScope.Trim(),
            SortOrder = maxSort + 1,
            IsDefaultOnImport = dto.IsDefaultOnImport,
            IsEnabled = dto.IsEnabled,
            IsLocked = false,
            Remark = string.IsNullOrWhiteSpace(dto.Remark) ? null : dto.Remark.Trim(),
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        _db.StdElementDicts.Add(entity);
        await _db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task<(bool Success, string Message)> UpdateAsync(ElementDictDto dto, string userName)
    {
        var entity = await _db.StdElementDicts.FirstOrDefaultAsync(x => x.Id == dto.Id);
        if (entity == null)
        {
            return (false, "字典项不存在");
        }

        entity.Name = (dto.Name ?? string.Empty).Trim();
        entity.Xlx = dto.Xlx;
        entity.Amount = dto.Amount > 0 ? dto.Amount : 1m;
        entity.Ggxh = string.IsNullOrWhiteSpace(dto.Ggxh) ? null : dto.Ggxh.Trim();
        entity.DefaultUnit = string.IsNullOrWhiteSpace(dto.DefaultUnit) ? null : dto.DefaultUnit.Trim();
        entity.TargetParentScope = string.IsNullOrWhiteSpace(dto.TargetParentScope) ? null : dto.TargetParentScope.Trim();
        entity.IsDefaultOnImport = dto.IsDefaultOnImport;
        entity.Remark = string.IsNullOrWhiteSpace(dto.Remark) ? null : dto.Remark.Trim();
        entity.UpdatedAt = DateTime.Now;
        entity.UpdatedBy = userName;

        await _db.SaveChangesAsync();
        return (true, "保存成功");
    }

    public async Task<(bool Success, string Message)> ToggleEnableAsync(int id, bool enabled, string userName)
    {
        var entity = await _db.StdElementDicts.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null)
        {
            return (false, "字典项不存在");
        }

        if (entity.IsLocked && !enabled)
        {
            return (false, $"{entity.Name} 为锁定项，不可停用");
        }

        entity.IsEnabled = enabled;
        entity.UpdatedAt = DateTime.Now;
        entity.UpdatedBy = userName;
        await _db.SaveChangesAsync();
        return (true, enabled ? "已启用" : "已停用");
    }

    public async Task<(bool Success, string Message)> ReorderAsync(
        byte level, IReadOnlyList<int> orderedIds, string reason, string userName)
    {
        if (orderedIds == null || orderedIds.Count == 0)
        {
            return (false, "排序列表为空");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return (false, "调整顺序必须填写理由");
        }

        var items = await _db.StdElementDicts
            .Where(x => x.Level == level)
            .ToListAsync();

        if (items.Count == 0)
        {
            return (false, "该级别无字典项");
        }

        // 校验 orderedIds 与现有项一一对应
        var existingIds = items.Select(x => x.Id).ToHashSet();
        if (orderedIds.Count != items.Count || !orderedIds.All(existingIds.Contains))
        {
            return (false, "排序列表与现有字典项不一致，请刷新后重试");
        }

        // 锁定项守卫：锁定项(器件)必须保持首位
        var lockedItem = items.FirstOrDefault(x => x.IsLocked);
        if (lockedItem != null && orderedIds[0] != lockedItem.Id)
        {
            return (false, $"{lockedItem.Name} 为固定首位，不可调整");
        }

        var beforeOrder = items.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => x.Name).ToList();

        var byId = items.ToDictionary(x => x.Id);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            var entity = byId[orderedIds[i]];
            entity.SortOrder = i + 1;
            entity.UpdatedAt = DateTime.Now;
            entity.UpdatedBy = userName;
        }

        await _db.SaveChangesAsync();

        var afterOrder = orderedIds.Select(id => byId[id].Name).ToList();
        await _auditLogService.WriteAsync(new AuditLogEntry
        {
            ActionType = "ReorderElementDict",
            Module = "ElementDict",
            EntityName = "STD_ELEMENT_DICT",
            EntityId = $"Level={level}",
            UserName = userName,
            IsSuccess = true,
            BeforeData = JsonSerializer.Serialize(new { order = beforeOrder }),
            AfterData = JsonSerializer.Serialize(new { order = afterOrder, reason })
        });

        return (true, "顺序已保存");
    }

    public async Task<IReadOnlyList<(string Name, int Xlx)>> GetDefaultImportLevel2Async()
    {
        var items = await _db.StdElementDicts.AsNoTracking()
            .Where(x => x.Level == 2 && x.IsEnabled && x.IsDefaultOnImport)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(x => new { x.Name, x.Xlx })
            .ToListAsync();

        return items.Select(x => (x.Name.Trim(), x.Xlx)).ToList();
    }

    private static ElementDictDto ToDto(StdElementDict e) => new()
    {
        Id = e.Id,
        Level = e.Level,
        Name = (e.Name ?? string.Empty).Trim(),
        Xlx = e.Xlx,
        Amount = e.Amount,
        Ggxh = e.Ggxh?.Trim(),
        DefaultUnit = e.DefaultUnit?.Trim(),
        TargetParentScope = e.TargetParentScope?.Trim(),
        SortOrder = e.SortOrder,
        IsDefaultOnImport = e.IsDefaultOnImport,
        IsEnabled = e.IsEnabled,
        IsLocked = e.IsLocked,
        Remark = e.Remark?.Trim(),
        UpdatedAt = e.UpdatedAt,
        UpdatedBy = e.UpdatedBy?.Trim()
    };
}
