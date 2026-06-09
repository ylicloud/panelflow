using PanelFlow.Core.Rules;
using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Extensions;

public static class QuotationQueryExtensions
{
    /// <summary>
    /// 筛选本人且未成立的报价单（与 <see cref="QuotationEditRules.CanOwnerOperate"/> 一致，可翻译为 SQL）。
    /// </summary>
    public static IQueryable<BjfatQuotation> WhereOwnerOperable(
        this IQueryable<BjfatQuotation> query, string loginUserName)
    {
        var user = loginUserName.Trim();
        return query.Where(q =>
            q.dqzt != QuotationEditRules.EstablishedStatus
            && q.bjr.Trim() == user);
    }
}
