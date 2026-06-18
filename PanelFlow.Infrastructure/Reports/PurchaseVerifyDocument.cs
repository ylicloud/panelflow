using PanelFlow.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PanelFlow.Infrastructure.Reports;

/// <summary>采购产品验证记录</summary>
public class PurchaseVerifyDocument : ReportDocumentBase
{
    private readonly PurchaseReport2DataDto _data;

    public PurchaseVerifyDocument(PurchaseReport2DataDto data) => _data = data;

    public override string Title => "采购产品验证记录";
    public override string DocumentNo => DocNoVerify;

    protected override void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(8).Text($"计划编号：{_data.PlanNo}").FontSize(9);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(24);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1.5f);
                    columns.RelativeColumn(1.8f);
                    columns.ConstantColumn(40);
                    columns.RelativeColumn(1.2f);
                    columns.ConstantColumn(36);
                    columns.ConstantColumn(36);
                    columns.ConstantColumn(36);
                    columns.ConstantColumn(36);
                    columns.ConstantColumn(36);
                    columns.ConstantColumn(52);
                    columns.ConstantColumn(40);
                    columns.ConstantColumn(44);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("序号");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("合同编号");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("产品名称");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("规格型号");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("数量");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("制造商");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("合格证");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("检验报告");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("外观");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("随机附件");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("随机资料");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("验证日期");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("结论");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("验证人");
                });

                foreach (var row in _data.Rows)
                {
                    var strike = row.IsDeleted ? TextStyle.Default.Strikethrough() : TextStyle.Default;

                    table.Cell().Element(CellStyle).AlignCenter().Text(row.Seq.ToString()).Style(strike);
                    table.Cell().Element(CellStyle).Text(row.ContractNo).Style(strike);
                    table.Cell().Element(CellStyle).Text(row.ItemName).Style(strike);
                    table.Cell().Element(CellStyle).Text(row.ItemSpec).Style(strike);
                    table.Cell().Element(CellStyle).AlignRight().Text(FormatQty(row.ItemQty)).Style(strike);
                    table.Cell().Element(CellStyle).Text(row.ItemManufacturer).Style(strike);
                    table.Cell().Element(CellStyle).AlignCenter().Text(row.HasCertText).Style(strike);
                    table.Cell().Element(CellStyle).AlignCenter().Text(row.HasInspectionText).Style(strike);
                    table.Cell().Element(CellStyle).AlignCenter().Text(row.AppearanceText).Style(strike);
                    table.Cell().Element(CellStyle).AlignCenter().Text(row.HasAccessoriesText).Style(strike);
                    table.Cell().Element(CellStyle).AlignCenter().Text(row.HasDocumentsText).Style(strike);
                    table.Cell().Element(CellStyle).AlignCenter().Text(row.VerifyDateText ?? string.Empty).Style(strike);
                    table.Cell().Element(CellStyle).AlignCenter().Text(row.Conclusion).Style(strike);
                    table.Cell().Element(CellStyle).AlignCenter().Text(row.Verifier).Style(strike);
                }
            });
        });
    }

    private static string FormatQty(decimal qty) =>
        qty % 1 == 0 ? ((int)qty).ToString() : qty.ToString("0.##");
}
