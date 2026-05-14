namespace PanelFlow.Core.Models;

public class AuditLogEntry
{
    public string ActionType { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? RoleName { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? BeforeData { get; set; }
    public string? AfterData { get; set; }
}
