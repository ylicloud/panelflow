using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface ICustomerService
{
    Task<List<CustomerDto>> GetListAsync(string? keyword);
    Task<CustomerDto?> GetByCompanyNoAsync(string companyNo);
    Task<(bool Success, string Message)> CreateAsync(CustomerDto dto, string? currentUser);
    Task<(bool Success, string Message)> UpdateAsync(CustomerDto dto, string? currentUser);
}
