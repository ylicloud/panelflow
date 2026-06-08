using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface IQuotationService
{
    Task<PagedResult<QuotationDto>> GetListAsync(string? keyword, bool includeHistory, int page, int pageSize);
    Task<QuotationDto?> GetByQuotationNoAsync(string quotationNo);
    Task<List<QuotationDto>> GetByCustomerNoAsync(string customerNo);
    Task<(bool Success, string Message)> CreateAsync(QuotationDto dto);
    Task<(bool Success, string Message)> UpdateAsync(QuotationDto dto, string operatorUserName);
    Task<(bool Success, string Message)> DeleteAsync(string quotationNo, string operatorUserName);
    Task<(bool Allowed, string Message)> ValidateEditAccessAsync(string quotationNo, string operatorUserName);
    Task<(bool CanRename, string Message)> CanRenameFabhAsync(string originalFabh, string operatorUserName);
    Task<RenameFabhResult> RenameFabhAsync(string originalFabh, string newFabh, string operatorUserName);
}
