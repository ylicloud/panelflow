using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface IPriceHistoryService
{
    Task<PriceHistoryListResult> ListHistoryAsync(
        string? keyword, bool onlySuspect, int page, int pageSize, string? sortBy = "ggxh", bool sortAsc = true);
    Task<IReadOnlyList<PriceSourceRowDto>> GetSourceRowsAsync(string xWzdh);
    Task<(bool Success, string Message)> AddExclusionAsync(string fabh, string? xWzdh, string reason, string userName);
    Task<(bool Success, string Message)> RemoveExclusionAsync(int id, string userName);
    Task<IReadOnlyList<PriceExclusionDto>> ListExclusionsAsync();
    Task<(bool Success, string Message)> RefreshHistoryAsync(string userName);
}
