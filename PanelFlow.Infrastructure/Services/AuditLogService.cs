using PanelFlow.Core.Interfaces;
using PanelFlow.Core.Models;
using PanelFlow.Infrastructure.Data;
using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _db;

    public AuditLogService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task WriteAsync(AuditLogEntry entry)
    {
        var entity = new SysAuditLog
        {
            ActionType = (entry.ActionType ?? string.Empty).Trim(),
            Module = (entry.Module ?? string.Empty).Trim(),
            EntityName = entry.EntityName?.Trim(),
            EntityId = entry.EntityId?.Trim(),
            UserName = (entry.UserName ?? string.Empty).Trim(),
            DisplayName = entry.DisplayName?.Trim(),
            RoleName = entry.RoleName?.Trim(),
            ClientIp = entry.ClientIp?.Trim(),
            UserAgent = entry.UserAgent?.Trim(),
            IsSuccess = entry.IsSuccess,
            ErrorMessage = entry.ErrorMessage?.Trim(),
            BeforeData = entry.BeforeData,
            AfterData = entry.AfterData,
            CreatedAt = DateTime.Now
        };

        _db.SysAuditLogs.Add(entity);
        await _db.SaveChangesAsync();
    }
}
