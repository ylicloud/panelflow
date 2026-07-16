using PanelFlow.Infrastructure.Entities;

namespace PanelFlow.Infrastructure.Services.QuotationSummary;

/// <summary>PB hz1/hz2/js 系列算法的内存行模型。</summary>
internal sealed class HzbWorkRow
{
    public string Fabh { get; set; } = string.Empty;
    public string Xbm { get; set; } = string.Empty;
    public string Xmc { get; set; } = string.Empty;
    public string Xdw { get; set; } = string.Empty;
    public decimal Xdj { get; set; }
    public decimal Xfdds { get; set; }
    public decimal Xsl { get; set; }
    public decimal XbjFdds { get; set; }
    public decimal XbjDj { get; set; }
    public decimal XbjbBj { get; set; }
    public decimal XbjbDj { get; set; }
    public DateTime? XbjbDatetime { get; set; }
    public decimal XbjbFdds { get; set; }
    public decimal Xwzfy { get; set; }
    public string Xflbh { get; set; } = string.Empty;
    public string Xggxh { get; set; } = string.Empty;
    public string Xsccj { get; set; } = string.Empty;
    public string XkeyRy { get; set; } = string.Empty;
    public decimal Xjsgsbh { get; set; }
    public string Xbz { get; set; } = string.Empty;
    public string Xwzdh { get; set; } = string.Empty;
    public int Xlx { get; set; }
    public int Xcgf { get; set; }
}

internal sealed class FlhzWorkRow
{
    public string Fabh { get; set; } = string.Empty;
    public string Xbm { get; set; } = string.Empty;
    public string Xmc { get; set; } = string.Empty;
    public string Xsm { get; set; } = string.Empty;
    public int Xlx { get; set; }
    public decimal Xsl { get; set; }
    public decimal Xwzfy { get; set; }
    public int Xcgf { get; set; }
    public string Xflbh { get; set; } = string.Empty;
    public string Xggxh { get; set; } = string.Empty;
    public string Xsccj { get; set; } = string.Empty;
    public string XkeyRy { get; set; } = string.Empty;
    public decimal Xzj1 { get; set; }
    public decimal Xzj1Bj { get; set; }
    public decimal Xzj1Jj { get; set; }
    public decimal Xzj1Scj { get; set; }
    public decimal Xzj1Zdj { get; set; }
    public decimal Xzj2 { get; set; }
    public decimal Xzj2Zdj { get; set; }
    public decimal Xzj3 { get; set; }
    public decimal Xzj3Zdj { get; set; }
    public decimal Xzj4 { get; set; }
    public decimal Xzj4Zdj { get; set; }
    public decimal Xzj5 { get; set; }
    public decimal Xzj5Zdj { get; set; }
    public decimal Xzj6 { get; set; }
    public decimal Xzj6Zdj { get; set; }
    public decimal Xzj7 { get; set; }
    public decimal Xzj7Zdj { get; set; }
    public decimal Xzj8 { get; set; }
    public decimal Xzj8Zdj { get; set; }
    public decimal Xzj9 { get; set; }
    public decimal Xzj9Zdj { get; set; }
    public decimal Xzj10 { get; set; }
    public decimal Xzj10Zdj { get; set; }
}

internal readonly record struct HzStackFrame(int Level, int Index, string Bm);

internal sealed class QuotationSummaryHzEngine
{
    private readonly IReadOnlyList<BjdBzbItem> _bzb;
    private readonly IReadOnlyList<BjdWybItem> _wyb;

    public QuotationSummaryHzEngine(IReadOnlyList<BjdBzbItem> bzb, IReadOnlyList<BjdWybItem> wyb)
    {
        _bzb = bzb;
        _wyb = wyb;
    }

    public static int GetTreeLevel(string bm) => bm.Trim().Length / 4 + 1;

    public bool TryHz1(List<HzbWorkRow> hzb, out string? error)
    {
        error = null;
        var count = hzb.Count;
        if (count <= 2)
            return true;

        hzb[0].Xsl = 1;
        hzb[0].Xfdds = 0;

        for (var i = 1; i < count - 1; i++)
        {
            var s2 = hzb[i].Xbm.Trim();
            var s1 = hzb[i - 1].Xbm.Trim();
            if (s2.Length <= s1.Length)
                continue;

            var parent = hzb[i - 1];
            parent.Xlx = 1;
            parent.Xdj = 0;
            parent.XbjDj = 0;
            parent.XbjbDj = 0;
            parent.XbjbBj = 0;
            parent.Xfdds = 0;
            parent.XbjFdds = 0;
            parent.XbjbFdds = 0;
            parent.Xwzfy = 0;
        }

        var stack = new List<HzStackFrame> { new(GetTreeLevel(hzb[0].Xbm), 0, hzb[0].Xbm.Trim()) };

        for (var i = 1; i < count - 1; i++)
        {
            var flbm = hzb[i].Xbm.Trim();
            var curjb = GetTreeLevel(flbm);

            while (curjb <= stack[^1].Level)
            {
                if (!TryJs1_1(hzb, stack[^1].Index, stack[^2].Index, out error))
                    return false;
                stack.RemoveAt(stack.Count - 1);
            }

            stack.Add(new HzStackFrame(curjb, i, flbm));

            if (i == count - 2)
            {
                curjb = 1;
                while (curjb < stack[^1].Level)
                {
                    if (!TryJs1_1(hzb, stack[^1].Index, stack[^2].Index, out error))
                        return false;
                    stack.RemoveAt(stack.Count - 1);
                }
            }
        }

        var last = hzb[^1];
        var first = hzb[0];
        last.Xdj = first.Xdj;
        last.XbjDj = first.XbjDj;
        last.XbjbDj = first.XbjbDj;
        last.XbjbBj = first.XbjbBj;
        last.Xwzfy = first.Xwzfy;
        last.Xsl = 1;
        last.Xfdds = 0;
        return true;
    }

    public bool TryBuildFlhzFramework(List<HzbWorkRow> hzb, List<FlhzWorkRow> flhz, out string? error)
    {
        error = null;
        if (flhz.Count > 0 || hzb.Count <= 0)
        {
            error = "分类汇总框架生成失败：目标表非空或源数据为空。";
            return false;
        }

        for (var i = 0; i < hzb.Count - 1; i++)
        {
            var src = hzb[i];
            var row = new FlhzWorkRow
            {
                Fabh = src.Fabh,
                Xbm = src.Xbm,
                Xmc = src.Xmc,
                Xflbh = src.Xflbh,
                Xggxh = src.Xggxh,
                Xsccj = src.Xsccj,
                XkeyRy = src.XkeyRy,
                Xsm = src.Xbm.Length >= 4 ? src.Xbm[..4] : src.Xbm,
                Xcgf = src.Xcgf,
                Xlx = src.Xlx,
                Xsl = src.Xsl,
                Xwzfy = src.Xwzfy
            };

            ApplyFlhzLeafMapping(row, src);
            flhz.Add(row);
        }

        var tail = hzb[^1];
        flhz.Add(new FlhzWorkRow
        {
            Fabh = tail.Fabh,
            Xbm = tail.Xbm,
            Xmc = tail.Xmc,
            Xsm = tail.Xggxh,
            Xlx = 1,
            Xsl = 1,
            Xwzfy = tail.Xwzfy
        });
        return true;
    }

    public bool TryHz2(List<FlhzWorkRow> flhz, out string? error)
    {
        error = null;
        var count = flhz.Count;
        if (count <= 0)
        {
            error = "分类汇总计算失败：无汇总行。";
            return false;
        }

        var stack = new List<HzStackFrame> { new(GetTreeLevel(flhz[0].Xbm), 0, flhz[0].Xbm.Trim()) };

        for (var i = 1; i < count - 1; i++)
        {
            var flbm = flhz[i].Xbm.Trim();
            var curjb = GetTreeLevel(flbm);

            if (curjb <= stack[^1].Level)
            {
                while (curjb <= stack[^1].Level)
                {
                    Js2_1(flhz, stack[^1].Index, stack[^2].Index);
                    stack.RemoveAt(stack.Count - 1);
                }
            }

            stack.Add(new HzStackFrame(curjb, i, flbm));

            if (i == count - 2)
            {
                curjb = 1;
                while (curjb < stack[^1].Level)
                {
                    Js2_1(flhz, stack[^1].Index, stack[^2].Index);
                    stack.RemoveAt(stack.Count - 1);
                }
            }
        }

        var last = flhz[^1];
        var first = flhz[0];
        last.Xzj1 = first.Xzj1;
        last.Xzj1Bj = first.Xzj1Bj;
        last.Xzj1Jj = first.Xzj1Jj;
        last.Xzj1Scj = first.Xzj1Scj;
        last.Xzj1Zdj = first.Xzj1Zdj;
        last.Xzj2 = first.Xzj2;
        last.Xzj3 = first.Xzj3;
        last.Xzj4 = first.Xzj4;
        last.Xzj5 = first.Xzj5;
        last.Xzj6 = first.Xzj6;
        last.Xzj7 = first.Xzj7;
        last.Xzj8 = first.Xzj8;
        last.Xzj9 = first.Xzj9;
        last.Xzj10 = first.Xzj10;
        last.Xzj2Zdj = first.Xzj2Zdj;
        last.Xzj3Zdj = first.Xzj3Zdj;
        last.Xzj4Zdj = first.Xzj4Zdj;
        last.Xzj5Zdj = first.Xzj5Zdj;
        last.Xzj6Zdj = first.Xzj6Zdj;
        last.Xzj7Zdj = first.Xzj7Zdj;
        last.Xzj8Zdj = first.Xzj8Zdj;
        last.Xzj9Zdj = first.Xzj9Zdj;
        last.Xzj10Zdj = first.Xzj10Zdj;
        last.Xsl = 1;
        return true;
    }

    private static void ApplyFlhzLeafMapping(FlhzWorkRow row, HzbWorkRow src)
    {
        switch (src.Xlx)
        {
            case 0:
                row.Xzj10Zdj = src.XbjDj;
                row.Xzj10 = src.Xdj;
                break;
            case 11:
                row.Xzj1 = src.Xdj;
                row.Xzj1Bj = src.Xdj;
                row.Xzj1Jj = src.XbjDj;
                row.Xzj1Scj = src.XbjbBj;
                row.Xzj1Zdj = src.XbjbDj;
                break;
            case 12:
                row.Xzj2Zdj = src.XbjDj;
                row.Xzj2 = src.Xdj;
                break;
            case 13:
                row.Xzj3Zdj = src.XbjDj;
                row.Xzj3 = src.Xdj;
                break;
            case 14:
                row.Xzj4Zdj = src.XbjDj;
                row.Xzj4 = src.Xdj;
                break;
            case 15:
                row.Xzj5Zdj = src.XbjDj;
                row.Xzj5 = src.Xdj;
                break;
            case 16:
                row.Xzj6Zdj = src.XbjDj;
                row.Xzj6 = src.Xdj;
                break;
            case 17:
                row.Xzj2Zdj = src.XbjDj;
                row.Xzj2 = src.Xdj;
                break;
            case 18:
                row.Xzj5Zdj = src.XbjDj;
                row.Xzj5 = src.Xdj;
                break;
            case 19:
                row.Xzj6Zdj = src.XbjDj;
                row.Xzj6 = src.Xdj;
                break;
        }
    }

    private bool TryJs1_1(List<HzbWorkRow> hzb, int xb1, int xb2, out string? error)
    {
        error = null;
        var child = hzb[xb1];
        var curlx = child.Xlx;
        var sk = child.Xggxh.Trim();
        var prefix = sk.Length >= 2 ? sk[..2] : string.Empty;
        var curjslx = prefix switch
        {
            "0:" => 0,
            "1:" => 1,
            "2:" => 2,
            _ => 0
        };

        switch (curlx)
        {
            case 0:
                if (curjslx == 1)
                    return TryJs1_2(hzb, xb1, xb2, out error);
                return TryJs1_3(hzb, xb1, xb2);
            case 1:
                return TryJs1_4(hzb, xb1, xb2);
            case 11:
                if (child.XbjbFdds < child.XbjFdds)
                {
                    child.XbjbFdds = child.XbjFdds;
                    child.XbjbDj = child.XbjDj;
                    var denom = 1 - child.XbjbFdds / 100m;
                    child.XbjbBj = denom == 0 ? 0 : Round2(child.XbjbDj / denom);
                }

                child.Xdj = Round2(child.XbjDj * (1 + child.Xfdds / 100m));
                AccumulateHz1ToParent(hzb, xb1, xb2);
                return true;
            case >= 12 and <= 14:
                if (curjslx == 1)
                    return TryJs1_2(hzb, xb1, xb2, out error);
                return TryJs1_3(hzb, xb1, xb2);
            case 15:
                return TryJs1_1Packaging(hzb, xb1, xb2, curjslx, out error);
            case 16:
                return TryJs1_1ScreenPrint(hzb, xb1, xb2, curjslx, out error);
            default:
                if (curjslx == 1)
                    return TryJs1_2(hzb, xb1, xb2, out error);
                return TryJs1_3(hzb, xb1, xb2);
        }
    }

    private bool TryJs1_1Packaging(List<HzbWorkRow> hzb, int xb1, int xb2, int curjslx, out string? error)
    {
        error = null;
        if (curjslx == 1)
            return TryJs1_2(hzb, xb1, xb2, out error);
        if (curjslx != 2)
            return TryJs1_3(hzb, xb1, xb2);

        var child = hzb[xb1];
        var str1 = child.Xggxh.Trim();
        if (str1.Length > 2)
            str1 = str1[2..];
        if (string.IsNullOrWhiteSpace(str1))
        {
            error = $"包装体积栏输入错误?项编号：{child.Xbm.Trim()}";
            return false;
        }

        var dl1 = str1.IndexOf('*');
        var dl2 = dl1 >= 0 ? str1.IndexOf('*', dl1 + 1) : -1;
        if (dl1 < 0 || dl2 < 0)
        {
            error = $"包装体积栏输入错误?项编号：{child.Xbm.Trim()}";
            return false;
        }

        var part1 = str1[..dl1];
        var part2 = str1[(dl1 + 1)..dl2];
        var part3 = str1[(dl2 + 1)..];
        if (!decimal.TryParse(part1, out var l) || !decimal.TryParse(part2, out var w) || !decimal.TryParse(part3, out var h))
        {
            error = $"包装体积栏输入错误?项编号：{child.Xbm.Trim()}";
            return false;
        }

        var tj = l * w * h / 1_000_000_000m;
        if (tj <= 0)
        {
            error = $"包装体积栏输入错误?项编号：{child.Xbm.Trim()}";
            return false;
        }

        decimal? unitPrice = null;
        foreach (var item in _bzb)
        {
            if (item.ZD is null || item.ZG is null || item.JG is null)
                continue;
            if (item.ZD <= tj && item.ZG >= tj)
            {
                unitPrice = item.JG * tj;
                break;
            }
        }

        if (unitPrice is null)
        {
            error = $"包装价格库中无此档次的包装单价?项编号：{child.Xbm.Trim()}";
            return false;
        }

        child.XbjFdds = 0;
        child.XbjbFdds = 0;
        child.Xfdds = 0;
        var price = Round2(unitPrice.Value);
        child.Xdj = price;
        child.XbjDj = price;
        child.XbjbDj = price;
        child.XbjbBj = price;
        AccumulateHz1ToParent(hzb, xb1, xb2);
        return true;
    }

    private bool TryJs1_1ScreenPrint(List<HzbWorkRow> hzb, int xb1, int xb2, int curjslx, out string? error)
    {
        error = null;
        if (curjslx == 1)
            return TryJs1_2(hzb, xb1, xb2, out error);
        if (curjslx != 2)
            return TryJs1_3(hzb, xb1, xb2);

        var child = hzb[xb1];
        var sk = child.Xbm.Trim();
        var raw = child.Xggxh.Trim();
        var str1 = raw.Length > 2 ? raw[2..] : string.Empty;
        if (string.IsNullOrWhiteSpace(str1))
        {
            error = $"网印说明栏输入错误?项编号：{sk}";
            return false;
        }

        var eqPos = str1.IndexOf('=');
        if (eqPos <= 0)
        {
            error = $"网印说明栏输入错误?项编号：{sk}";
            return false;
        }

        var str2 = str1[..eqPos];
        var sizePart = str1[(eqPos + 1)..];
        var starPos = sizePart.IndexOf('*');
        if (starPos <= 0)
        {
            error = $"网印说明栏输入错误?项编号：{sk}";
            return false;
        }

        if (!decimal.TryParse(sizePart[..starPos], out var width) ||
            !decimal.TryParse(sizePart[(starPos + 1)..], out var height))
        {
            error = $"网印说明栏输入错误?项编号：{sk}";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            error = $"网印说明栏输入错误?项编号：{sk}";
            return false;
        }

        var area = width * height / 10_000m;
        BjdWybItem? matched = null;
        foreach (var item in _wyb)
        {
            if (string.Equals(item.MC.Trim(), str2.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                matched = item;
                break;
            }
        }

        if (matched is null)
        {
            error = $"网印价格库中没有此类型的网印单价?项编号：{sk}";
            return false;
        }

        var price = Round2(area * matched.DJ);
        child.XbjFdds = 0;
        child.XbjbFdds = 0;
        child.Xfdds = 0;
        child.XbjDj = price;
        child.Xdj = price;
        child.XbjbDj = price;
        child.XbjbBj = price;
        AccumulateHz1ToParent(hzb, xb1, xb2);
        return true;
    }

    private bool TryJs1_2(List<HzbWorkRow> hzb, int xb1, int xb2, out string? error)
    {
        error = null;
        var child = hzb[xb1];
        var curstr = child.Xggxh.Trim();
        if (curstr.Length <= 2)
        {
            error = $"编号：{child.Xbm.Trim()}==项名称：{child.Xmc.Trim()}";
            return false;
        }

        var bases = curstr[2..].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (bases.Count == 0)
        {
            error = $"编号：{child.Xbm.Trim()}==项名称：{child.Xmc.Trim()}";
            return false;
        }

        child.XbjFdds = 0;
        child.XbjbFdds = 0;
        child.Xfdds = 0;
        child.Xdj = 0;
        child.XbjDj = 0;
        child.XbjbDj = 0;
        child.XbjbBj = 0;

        foreach (var basisName in bases)
        {
            var k2 = FindBasisRowIndex(hzb, xb1, xb2, basisName);
            if (k2 < 0)
            {
                error = $"计算行的基数栏《说明栏》输入错误!编号：{child.Xbm.Trim()}==项名称：{child.Xmc.Trim()}";
                return false;
            }

            var ratio = child.Xjsgsbh / 100m;
            child.Xdj += Round2(hzb[k2].Xdj * ratio);
            child.XbjDj += Round2(hzb[k2].XbjDj * ratio);
            child.XbjbDj += Round2(hzb[k2].XbjbDj * ratio);
            child.XbjbBj += Round2(hzb[k2].XbjbBj * ratio);
            child.Xfdds = 0;
            child.XbjFdds = 0;
            child.XbjbFdds = 0;
        }

        AccumulateHz1ToParent(hzb, xb1, xb2);
        return true;
    }

    private static int FindBasisRowIndex(List<HzbWorkRow> hzb, int xb1, int xb2, string basisName)
    {
        var cu = hzb[xb1].Xbm.Trim();
        var le = cu.Length;
        if (le > 4)
        {
            le -= 4;
            var prefix = cu[..le];
            for (var k1 = xb2; k1 < xb1; k1++)
            {
                var rowBm = hzb[k1].Xbm.Trim();
                if (rowBm.Length >= le && rowBm[..le] == prefix &&
                    string.Equals(hzb[k1].Xmc.Trim(), basisName, StringComparison.OrdinalIgnoreCase))
                    return k1;
            }
        }
        else
        {
            for (var k1 = xb2; k1 < xb1; k1++)
            {
                if (hzb[k1].Xbm.Trim().Length == 4 &&
                    string.Equals(hzb[k1].Xmc.Trim(), basisName, StringComparison.OrdinalIgnoreCase))
                    return k1;
            }
        }

        return -1;
    }

    private static bool TryJs1_3(List<HzbWorkRow> hzb, int xb1, int xb2)
    {
        var child = hzb[xb1];
        child.XbjFdds = 0;
        child.XbjbFdds = 0;
        child.XbjbDj = child.XbjDj;
        AccumulateHz1ToParent(hzb, xb1, xb2);
        return true;
    }

    private static bool TryJs1_4(List<HzbWorkRow> hzb, int xb1, int xb2)
    {
        AccumulateHz1ToParent(hzb, xb1, xb2);
        return true;
    }

    private static void AccumulateHz1ToParent(List<HzbWorkRow> hzb, int xb1, int xb2)
    {
        var child = hzb[xb1];
        var parent = hzb[xb2];
        var sl = child.Xsl;
        parent.Xdj += Round2(child.Xdj * sl);
        parent.XbjDj += Round2(child.XbjDj * sl);
        parent.XbjbDj += Round2(child.XbjbDj * sl);
        parent.XbjbBj += Round2(child.XbjbBj * sl);
        parent.Xwzfy += Round2(child.Xwzfy * sl);
    }

    private static void Js2_1(List<FlhzWorkRow> flhz, int xb1, int xb2)
    {
        var child = flhz[xb1];
        var parent = flhz[xb2];
        var sl = child.Xsl;

        parent.Xzj1 += Round2(child.Xzj1 * sl);
        parent.Xzj1Bj += Round2(child.Xzj1Bj * sl);
        parent.Xzj1Jj += Round2(child.Xzj1Jj * sl);
        parent.Xzj1Scj += Round2(child.Xzj1Scj * sl);
        parent.Xzj1Zdj += Round2(child.Xzj1Zdj * sl);
        parent.Xzj2 += Round2(child.Xzj2 * sl);
        parent.Xzj3 += Round2(child.Xzj3 * sl);
        parent.Xzj4 += Round2(child.Xzj4 * sl);
        parent.Xzj5 += Round2(child.Xzj5 * sl);
        parent.Xzj6 += Round2(child.Xzj6 * sl);
        parent.Xzj7 += Round2(child.Xzj7 * sl);
        parent.Xzj8 += Round2(child.Xzj8 * sl);
        parent.Xzj9 += Round2(child.Xzj9 * sl);
        parent.Xzj10 += Round2(child.Xzj10 * sl);
        parent.Xzj2Zdj += Round2(child.Xzj2Zdj * sl);
        parent.Xzj3Zdj += Round2(child.Xzj3Zdj * sl);
        parent.Xzj4Zdj += Round2(child.Xzj4Zdj * sl);
        parent.Xzj5Zdj += Round2(child.Xzj5Zdj * sl);
        parent.Xzj6Zdj += Round2(child.Xzj6Zdj * sl);
        parent.Xzj7Zdj += Round2(child.Xzj7Zdj * sl);
        parent.Xzj8Zdj += Round2(child.Xzj8Zdj * sl);
        parent.Xzj9Zdj += Round2(child.Xzj9Zdj * sl);
        parent.Xzj10Zdj += Round2(child.Xzj10Zdj * sl);
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
