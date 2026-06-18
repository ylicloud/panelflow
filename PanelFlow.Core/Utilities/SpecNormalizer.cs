using System.Text;

namespace PanelFlow.Core.Utilities;

/// <summary>
/// C# 版 F_CleanString：转小写 → 删除不可见字符 → 全角转半角 → 去掉括号内容 → 只保留字母/数字/中文/单位符号。
/// 用于生成型号规格的标准化指纹字符串（x_wzdh），便于与历史报价比对。
/// </summary>
public static class SpecNormalizer
{
    private const string UnitSymbols = "μωΩ°±℃φ";

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var s = input.ToLowerInvariant()
            .Replace("\r", "").Replace("\n", "").Replace("\t", "")
            .Replace("\u00A0", "")
            .Replace("\u200B", "");

        var sb = new StringBuilder(s.Length);
        var parenDepth = 0;

        foreach (var ch in s)
        {
            var c = ch == '\u3000' ? ' '
                  : ch >= '\uFF01' && ch <= '\uFF5E' ? (char)(ch - 65248)
                  : ch;
            c = char.ToLowerInvariant(c);

            if (c == '(') { parenDepth++; continue; }
            if (c == ')') { if (parenDepth > 0) parenDepth--; continue; }
            if (parenDepth > 0) continue;

            if (char.IsAsciiLetterOrDigit(c) || (c >= '\u4E00' && c <= '\u9FFF'))
                sb.Append(c);
            else if (UnitSymbols.IndexOf(c) >= 0)
                sb.Append(c);
        }

        return sb.ToString();
    }
}
