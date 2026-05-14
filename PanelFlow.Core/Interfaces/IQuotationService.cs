using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface IQuotationService
{
    Task<PagedResult<QuotationDto>> GetListAsync(string? keyword, bool includeHistory, int page, int pageSize);
    Task<QuotationDto?> GetByQuotationNoAsync(string quotationNo);
    Task<List<QuotationDto>> GetByCustomerNoAsync(string customerNo);
    Task<(bool Success, string Message)> CreateAsync(QuotationDto dto);
    Task<(bool Success, string Message)> UpdateAsync(QuotationDto dto);
    Task<(bool Success, string Message)> DeleteAsync(string quotationNo, string operatorUserName);
}
