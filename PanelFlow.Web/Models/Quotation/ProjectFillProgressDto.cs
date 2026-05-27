namespace PanelFlow.Web.Models.Quotation;

/// <summary>
/// 报价单填价进度看板 DTO。覆盖 fill-progress-dashboard spec / Req B-1~B-3。
/// </summary>
public sealed class ProjectFillProgressDto
{
    /// <summary>元件行总数（x_lx=11 且 x_bm 长度=12 且 Substring(4,4)="0001"）</summary>
    public int TotalRows { get; init; }

    /// <summary>已填价行数（x_bj_dj &gt; 0）</summary>
    public int FilledRows { get; init; }

    /// <summary>未填价行数（x_bj_dj &lt;= 0 或为空）</summary>
    public int UnfilledRows { get; init; }

    /// <summary>4 类异常的分项与总和</summary>
    public AnomalyCountDto Anomalies { get; init; } = new();

    /// <summary>问题清单明细，供前端"问题清单抽屉"展示与跳转使用</summary>
    public IReadOnlyList<ProblemItemDto> Problems { get; init; } = Array.Empty<ProblemItemDto>();
}

/// <summary>
/// 4 类异常计数器。互斥规则见 spec §2.Req B-2 与 P-B2。
/// </summary>
public sealed class AnomalyCountDto
{
    /// <summary>x_bj_dj &lt; 0</summary>
    public int Negative { get; init; }

    /// <summary>x_bj_dj &gt; 0 且偏离 STD_PRICE_HISTORY.avg_price 超过 ±20%</summary>
    public int Deviation { get; init; }

    /// <summary>x_wzdh 为空白（无法做历史价匹配）</summary>
    public int MissingSpec { get; init; }

    /// <summary>x_bj_dj == 0</summary>
    public int ZeroPrice { get; init; }

    /// <summary>4 类之和，方便前端徽章直接渲染</summary>
    public int Total => Negative + Deviation + MissingSpec + ZeroPrice;
}

/// <summary>
/// 单条问题明细。前端按 CabinetCode 分组渲染，点击可跳转到对应柜的对应行。
/// </summary>
public sealed class ProblemItemDto
{
    /// <summary>柜编码（x_bm 截取前 4 位）</summary>
    public string CabinetCode { get; init; } = string.Empty;

    /// <summary>柜名（来自柜行 x_mc；为空则回退到 CabinetCode）</summary>
    public string CabinetName { get; init; } = string.Empty;

    /// <summary>柜内行序号（按 x_bm 字典序枚举，从 1 开始）</summary>
    public int RowSeq { get; init; }

    /// <summary>元件名称</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>规格型号</summary>
    public string Spec { get; init; } = string.Empty;

    /// <summary>当前单价（x_bj_dj）</summary>
    public decimal CurrentPrice { get; init; }

    /// <summary>STD_PRICE_HISTORY.avg_price；无历史时为 null</summary>
    public decimal? AvgPrice { get; init; }

    /// <summary>问题分类：negative / deviation / missing_spec / zero_price</summary>
    public string IssueType { get; init; } = string.Empty;
}

/// <summary>
/// 异常分类常量，避免前后端魔法字符串散落。
/// </summary>
public static class IssueTypes
{
    public const string Negative = "negative";
    public const string Deviation = "deviation";
    public const string MissingSpec = "missing_spec";
    public const string ZeroPrice = "zero_price";
}

/// <summary>
/// 进度计算的纯函数输入。Controller 端做 IO 后构造此列表交给 FillProgressCalculator，
/// 使核心归类逻辑可被属性测试快速覆盖（无需启动 WebApplicationFactory）。
/// 调用方需自行保证：列表内仅含元件行，并按 (CabinetCode, x_bm) 字典序排好序——
/// 计算器据此为每个柜从 1 起递增 RowSeq。
/// </summary>
internal sealed record FillProgressComponentInput(
    string CabinetCode,
    string CabinetName,
    string Name,
    string Spec,
    string EffectiveWzdh,
    decimal Price);

/// <summary>
/// 进度计算的纯函数。
/// 异常归类规则（与 spec / Req B-2 一致）：
///   * negative 与 zero_price 互斥（同一行价格不可能既负又零）
///   * deviation 与 missing_spec 与 negative/zero_price 可共存（同一行可能"缺规格"且"价格为负"）
///   * 抽屉中每条异常生成 1 个 ProblemItemDto，按 issue 类别独立计数
/// </summary>
internal static class FillProgressCalculator
{
    /// <summary>
    /// 计算填价进度与异常聚合。
    /// </summary>
    /// <param name="components">已经按 (CabinetCode, x_bm) 字典序排好的元件行列表</param>
    /// <param name="avgByWzdh">x_wzdh → STD_PRICE_HISTORY.avg_price（OrdinalIgnoreCase）</param>
    public static ProjectFillProgressDto Calculate(
        IReadOnlyList<FillProgressComponentInput> components,
        IReadOnlyDictionary<string, decimal?> avgByWzdh)
    {
        if (components is null) throw new ArgumentNullException(nameof(components));
        if (avgByWzdh is null) throw new ArgumentNullException(nameof(avgByWzdh));

        if (components.Count == 0)
        {
            return new ProjectFillProgressDto();
        }

        var filled = 0;
        var unfilled = 0;
        var negative = 0;
        var deviation = 0;
        var missingSpec = 0;
        var zeroPrice = 0;
        var problems = new List<ProblemItemDto>(capacity: Math.Min(components.Count, 256));
        var seqByCabinet = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var c in components)
        {
            var cabCode = c.CabinetCode ?? string.Empty;
            seqByCabinet.TryGetValue(cabCode, out var seq);
            seq++;
            seqByCabinet[cabCode] = seq;

            var price = c.Price;
            if (price > 0m) filled++;
            else unfilled++;

            var avgPrice = !string.IsNullOrEmpty(c.EffectiveWzdh)
                && avgByWzdh.TryGetValue(c.EffectiveWzdh, out var avg) ? avg : null;

            string? issueType = null;
            if (price < 0m)
            {
                negative++;
                issueType = IssueTypes.Negative;
            }
            else if (price == 0m)
            {
                zeroPrice++;
                issueType = IssueTypes.ZeroPrice;
            }
            else if (avgPrice.HasValue && avgPrice.Value > 0m)
            {
                var dev = Math.Abs(price - avgPrice.Value) / avgPrice.Value;
                if (dev > 0.20m)
                {
                    deviation++;
                    issueType = IssueTypes.Deviation;
                }
            }

            if (string.IsNullOrEmpty(c.EffectiveWzdh))
            {
                missingSpec++;
                problems.Add(new ProblemItemDto
                {
                    CabinetCode = cabCode,
                    CabinetName = string.IsNullOrEmpty(c.CabinetName) ? cabCode : c.CabinetName,
                    RowSeq = seq,
                    Name = c.Name ?? string.Empty,
                    Spec = c.Spec ?? string.Empty,
                    CurrentPrice = price,
                    AvgPrice = avgPrice,
                    IssueType = IssueTypes.MissingSpec
                });
            }

            if (issueType != null)
            {
                problems.Add(new ProblemItemDto
                {
                    CabinetCode = cabCode,
                    CabinetName = string.IsNullOrEmpty(c.CabinetName) ? cabCode : c.CabinetName,
                    RowSeq = seq,
                    Name = c.Name ?? string.Empty,
                    Spec = c.Spec ?? string.Empty,
                    CurrentPrice = price,
                    AvgPrice = avgPrice,
                    IssueType = issueType
                });
            }
        }

        problems.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.CabinetCode, b.CabinetCode);
            if (c != 0) return c;
            c = a.RowSeq.CompareTo(b.RowSeq);
            if (c != 0) return c;
            return string.CompareOrdinal(a.IssueType, b.IssueType);
        });

        return new ProjectFillProgressDto
        {
            TotalRows = components.Count,
            FilledRows = filled,
            UnfilledRows = unfilled,
            Anomalies = new AnomalyCountDto
            {
                Negative = negative,
                Deviation = deviation,
                MissingSpec = missingSpec,
                ZeroPrice = zeroPrice
            },
            Problems = problems
        };
    }
}
