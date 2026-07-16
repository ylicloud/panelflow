using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

/// <summary>报价单 PB 正式汇总：写 BJB_HZB / BJB_XMYJB / BJB_XMYJHZ / BJB_XMHZ。</summary>
public interface IQuotationSummaryService
{
    Task<QuotationSummaryPrecheckResult> PrecheckAsync(string fabh, CancellationToken cancellationToken = default);

    Task<QuotationSummaryRunResult> RunSummaryAsync(
        string fabh,
        bool ignoreEmptyComponentWarning,
        Func<string, Task>? reportProgress = null,
        CancellationToken cancellationToken = default);

    Task<QuotationSummaryStatusDto> GetStatusAsync(string fabh, CancellationToken cancellationToken = default);

    Task<QuotationSummaryPageDto?> GetPageAsync(string fabh, string loginUserName, CancellationToken cancellationToken = default);
}
