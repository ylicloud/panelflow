using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface ICustomerContactService
{
    Task<List<CustomerContactDto>> GetByCompanyNoAsync(string companyNo);
    Task<(bool Success, string Message)> CreateAsync(CustomerContactDto dto);
    Task<(bool Success, string Message)> UpdateAsync(CustomerContactDto dto);
    Task<(bool Success, string Message)> DeleteAsync(string companyNo, int id);
    Task<(bool Success, string Message)> SetDefaultAsync(string companyNo, int id);
}
