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

    /// <summary>
    /// 浅拷贝报价单：复制 BJFAT 头 + 全部 BJB 明细到新编号（不含汇总/合同/采购）。
    /// </summary>
    Task<(bool Success, string Message, string? NewFabh)> CloneAsync(
        string sourceFabh,
        string newFabh,
        string? newName,
        string? customerNo,
        string? remark,
        string operatorUserName);
}
