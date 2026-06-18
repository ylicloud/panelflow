using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

public interface IPriceQueryService
{
    Task<PriceQueryResultDto> QueryBySpecAsync(string? spec, string? wzdh);
    Task<PriceBatchQueryResultDto> QueryBatchAsync(PriceBatchQueryRequest request);
}
