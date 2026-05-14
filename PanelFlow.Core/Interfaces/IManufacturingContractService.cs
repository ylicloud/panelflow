using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface IManufacturingContractService
{
    Task<PagedResult<ManufacturingContractDto>> GetListAsync(string? keyword, bool includeHistory, int page, int pageSize);
    Task<ManufacturingContractDto?> GetByContractNoAsync(string contractNo);
    Task<(bool Success, string Message)> CreateAsync(ManufacturingContractDto dto);
    Task<(bool Success, string Message)> UpdateAsync(ManufacturingContractDto dto);
    Task<(bool Success, string Message)> DeleteAsync(string contractNo);
}
