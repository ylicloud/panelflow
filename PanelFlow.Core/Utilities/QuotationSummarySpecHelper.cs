namespace PanelFlow.Core.Utilities;

/// <summary>PB 汇总阶段 D 的规格标准化：trim、空规格回退名称、全角横杠转半角。</summary>
public static class QuotationSummarySpecHelper
{
    public static string NormalizeGgxh(string? ggxh, string? nameFallback)
    {
        var cur = (ggxh ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(cur))
            cur = (nameFallback ?? string.Empty).Trim();

        while (true)
        {
            var pos = cur.IndexOf('－', StringComparison.Ordinal);
            if (pos < 0)
                break;
            cur = cur.Remove(pos, 1).Insert(pos, "-");
        }

        return cur;
    }
}
