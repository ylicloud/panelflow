using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PanelFlow.Infrastructure.Reports;

/// <summary>军标报表基类：A4、页眉页脚、中文字体。</summary>
public abstract class ReportDocumentBase : IDocument
{
    protected const string DocNoPlan = "Q/XZS GZ04.070-2025";
    protected const string DocNoVerify = "Q/XZS GZ04.071-2025";

    private static bool _fontRegistered;

    static ReportDocumentBase()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        RegisterChineseFont();
    }

    public abstract string Title { get; }
    public abstract string DocumentNo { get; }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(36);
            page.MarginVertical(28);
            page.DefaultTextStyle(x => x.FontSize(9).FontFamily(GetFontFamily()));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    protected abstract void ComposeContent(IContainer container);

    protected virtual void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().AlignCenter().Text(Title).FontSize(14).Bold();
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text($"编号：{DocumentNo}").FontSize(8);
                row.RelativeItem().AlignRight().Text($"打印日期：{DateTime.Now:yyyy-MM-dd}").FontSize(8);
            });
            col.Item().PaddingTop(6).LineHorizontal(0.5f);
        });
    }

    protected virtual void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text("编制：____________").FontSize(8);
                row.RelativeItem().AlignCenter().Text("审核：____________").FontSize(8);
                row.RelativeItem().AlignRight().Text("单位负责人：____________").FontSize(8);
            });
            col.Item().PaddingTop(2).AlignCenter().Text(text =>
            {
                text.Span("第 ").FontSize(8);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" / ").FontSize(8);
                text.TotalPages().FontSize(8);
                text.Span(" 页").FontSize(8);
            });
        });
    }

    protected static IContainer CellStyle(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(3);

    protected static IContainer HeaderCellStyle(IContainer c) =>
        CellStyle(c).Background(Colors.Grey.Lighten3).DefaultTextStyle(x => x.Bold());

    private static void RegisterChineseFont()
    {
        if (_fontRegistered) return;

        var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var candidates = new[] { "msyh.ttc", "msyhbd.ttc", "simhei.ttf", "simsun.ttc" };
        foreach (var file in candidates)
        {
            var path = Path.Combine(fontsDir, file);
            if (!File.Exists(path)) continue;
            try
            {
                using var stream = File.OpenRead(path);
                FontManager.RegisterFont(stream);
                _fontRegistered = true;
                return;
            }
            catch
            {
                // 尝试下一个字体
            }
        }
    }

    private static string GetFontFamily()
    {
        RegisterChineseFont();
        return _fontRegistered ? "Microsoft YaHei" : "Arial";
    }
}
