using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface IPurchaseService
{
    Task<PagedResult<PurchasePlanDto>> GetPlanListAsync(
        string? keyword, short? statusFilter, int page, int pageSize, bool issuedOnly = false);

    Task<PurchasePlanDto?> GetPlanByIdAsync(int planId, bool includeItems = true);

    Task<(bool Success, string Message, int? PlanId)> CreatePlanFromFabhAsync(
        string fabh, string creator);

    Task<(bool Success, string Message)> SavePlanItemsAsync(
        int planId, IReadOnlyList<PurchasePlanItemDto> items);

    Task<(bool Success, string Message)> IssuePlanAsync(int planId, string issuedBy);

    Task<(bool Success, string Message)> SaveVerificationAsync(
        int planId, IReadOnlyList<PurchasePlanItemDto> items);

    Task<PurchaseReport1DataDto?> GetReport1DataAsync(int planId, bool showDeleted);

    Task<PurchaseReport2DataDto?> GetReport2DataAsync(int planId, bool showDeleted);

    Task<bool> HasSummaryDataAsync(string fabh);
}
