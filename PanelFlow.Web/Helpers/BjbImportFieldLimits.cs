using System.Globalization;
using System.Text.RegularExpressions;

namespace PanelFlow.Web.Helpers;

/// <summary>
/// BJB 导入字段长度限制，与 databaseStructure.csv 中 BJB 表定义一致。
/// 校验口径对齐 SQL Server <c>LEN(LTRIM(RTRIM(col)))</c>（按字符数，非字节数）。
/// </summary>
internal static class BjbImportFieldLimits
{
    public const int XMc = 50;
    public const int XGgxh = 50;
    public const int XSccj = 50;
    public const int XWzdh = 100;

    /// <summary>
    /// 与 SQL Server LEN(LTRIM(RTRIM(@s))) 一致：Trim 后按 UTF-16 码元计数。
    /// char/nvarchar 长度限制均按字符数计；中文、英文均各计 1 字符。
    /// </summary>
    public static int SqlLen(string? value) => (value ?? string.Empty).Trim().Length;

    public static string Limit(string? value, int maxLen)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= maxLen ? text : text[..maxLen];
    }

    /// <summary>
    /// 校验导入表格与目录树名称长度，返回可展示给用户的错误摘要（1 基行号）。
    /// </summary>
    public static List<string> ValidateImportTable(
        IReadOnlyList<IReadOnlyList<string?>> tableRows,
        IReadOnlyList<string> treeNodeNames,
        Func<string?, string> normalizeSpec)
    {
        var errors = new List<string>();
        var sourceUnits = ParseSourceUnitsForValidation(tableRows);

        var expectedNodeCount = sourceUnits.Sum(u => u.SplitCount);
        if (expectedNodeCount != treeNodeNames.Count)
        {
            return errors;
        }

        var treeIndex = 0;
        foreach (var sourceUnit in sourceUnits)
        {
            for (var splitIndex = 0; splitIndex < sourceUnit.SplitCount; splitIndex++)
            {
                var unitNodeName = treeNodeNames[treeIndex];
                treeIndex += 1;
                AppendLengthError(errors, unitNodeName, XMc,
                    $"目录树第 {treeIndex} 个控制柜名称超过 {XMc} 字（当前 {SqlLen(unitNodeName)} 字），请缩短后重试");
            }

            foreach (var component in sourceUnit.Components)
            {
                var rowNo = component.SourceRowNo;
                AppendColumnLengthError(errors, rowNo, 3, component.Name, XMc, "名称");
                AppendColumnLengthError(errors, rowNo, 4, component.Spec, XGgxh, "规格");
                AppendColumnLengthError(errors, rowNo, 7, component.Vendor, XSccj, "生产厂家");

                var wzdh = normalizeSpec(component.Spec);
                if (SqlLen(wzdh) > XWzdh)
                {
                    errors.Add($"第 {rowNo} 行：规格标准化指纹超过 {XWzdh} 字（当前 {SqlLen(wzdh)} 字），请缩短规格后重试");
                }
            }
        }

        foreach (var unit in sourceUnits)
        {
            if (unit.SourceRowNo <= 0)
            {
                continue;
            }

            var splitNames = BuildSplitNames(unit.UnitNo, unit.SplitCount);
            for (var i = 0; i < splitNames.Count; i++)
            {
                var name = splitNames[i];
                if (SqlLen(name) > XMc)
                {
                    errors.Add(
                        $"第 {unit.SourceRowNo} 行：单元号拆分后的控制柜名称超过 {XMc} 字（第 {i + 1} 个：「{name}」，当前 {SqlLen(name)} 字），请缩短单元号或降低拆分数量");
                }
            }
        }

        return errors;
    }

    private static void AppendColumnLengthError(
        List<string> errors,
        int rowNo,
        int columnNo,
        string? value,
        int maxLen,
        string columnLabel)
    {
        var len = SqlLen(value);
        if (len > maxLen)
        {
            errors.Add($"第 {rowNo} 行：第{columnNo}列{columnLabel}超过 {maxLen} 字（当前 {len} 字），请缩短后重试");
        }
    }

    private static void AppendLengthError(List<string> errors, string? value, int maxLen, string message)
    {
        if (SqlLen(value) > maxLen)
        {
            errors.Add(message);
        }
    }

    private static List<SourceUnitValidationBlock> ParseSourceUnitsForValidation(
        IReadOnlyList<IReadOnlyList<string?>> tableRows)
    {
        var units = new List<SourceUnitValidationBlock>();
        SourceUnitValidationBlock? currentUnit = null;

        for (var rowIndex = 0; rowIndex < tableRows.Count; rowIndex++)
        {
            var row = NormalizeColumns(tableRows[rowIndex]);
            var rowNo = rowIndex + 1;
            var c2UnitMarker = row[1];
            var c3Name = row[2];
            var c4Spec = row[3];
            var c5Price = row[4];
            var c6Qty = row[5];
            var c7Vendor = row[6];

            if (!string.IsNullOrWhiteSpace(c2UnitMarker))
            {
                currentUnit = new SourceUnitValidationBlock
                {
                    SourceRowNo = rowNo,
                    UnitNo = c2UnitMarker,
                    SplitCount = ParseSplitCount(c6Qty)
                };
                units.Add(currentUnit);
                continue;
            }

            if (currentUnit == null)
            {
                continue;
            }

            var hasComponentData = !string.IsNullOrWhiteSpace(c3Name)
                                   || !string.IsNullOrWhiteSpace(c4Spec)
                                   || !string.IsNullOrWhiteSpace(c5Price)
                                   || !string.IsNullOrWhiteSpace(c6Qty)
                                   || !string.IsNullOrWhiteSpace(c7Vendor);
            if (!hasComponentData)
            {
                continue;
            }

            currentUnit.Components.Add(new ComponentValidationRow
            {
                SourceRowNo = rowNo,
                Name = c3Name,
                Spec = c4Spec,
                Vendor = c7Vendor
            });
        }

        return units;
    }

    private static List<string> NormalizeColumns(IReadOnlyList<string?>? source)
    {
        var normalized = new List<string>(8);
        for (var i = 0; i < 8; i++)
        {
            var value = source != null && i < source.Count ? source[i] : string.Empty;
            normalized.Add((value ?? string.Empty).Trim());
        }

        return normalized;
    }

    private static int ParseSplitCount(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
        }

        if (value <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Floor(value));
    }

    /// <summary>
    /// 与前端 buildSplitNames 及 SavePlan 目录预览命名规则保持一致。
    /// </summary>
    internal static List<string> BuildSplitNames(string baseName, int n)
    {
        var trimmed = (baseName ?? string.Empty).Trim();
        var safeCount = n;
        if (safeCount < 1)
        {
            safeCount = 1;
        }

        if (safeCount > 99)
        {
            safeCount = 99;
        }

        if (safeCount == 1)
        {
            return [trimmed];
        }

        var match = Regex.Match(trimmed, @"^(.*?)(\d+)$");
        if (match.Success)
        {
            var prefix = match.Groups[1].Value;
            var rawNumber = match.Groups[2].Value;
            var width = rawNumber.Length;
            var current = int.Parse(rawNumber, CultureInfo.InvariantCulture);
            var names = new List<string> { trimmed };
            for (var i = 1; i < safeCount; i++)
            {
                current += 1;
                names.Add($"{prefix}{current.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}");
            }

            return names;
        }

        var numbered = new List<string>();
        for (var i = 1; i <= safeCount; i++)
        {
            numbered.Add($"{trimmed}{i}");
        }

        return numbered;
    }

    private sealed class SourceUnitValidationBlock
    {
        public int SourceRowNo { get; set; }
        public string UnitNo { get; set; } = string.Empty;
        public int SplitCount { get; set; } = 1;
        public List<ComponentValidationRow> Components { get; set; } = [];
    }

    private sealed class ComponentValidationRow
    {
        public int SourceRowNo { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Spec { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
    }
}
