using PanelFlow.Core.Models;

namespace PanelFlow.Core.Interfaces;

/// <summary>
/// 报价单结构维护：构树、挂入/删除/改名/排序、按位置重编码写回 BJB。
/// </summary>
public interface IQuotationStructureService
{
    Task<QuotationStructureDto?> GetTreeAsync(string fabh, string loginUserName, string loginRole);

    Task<StructureApplyResult> ApplyAsync(StructureApplyRequest request, string loginUserName, string loginRole);
}
