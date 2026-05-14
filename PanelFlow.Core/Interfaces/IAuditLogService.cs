using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface IAuditLogService
{
    Task WriteAsync(AuditLogEntry entry);
}
