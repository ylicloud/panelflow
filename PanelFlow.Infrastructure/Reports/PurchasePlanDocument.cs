using PanelFlow.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PanelFlow.Infrastructure.Reports;

/// <summary>合同配套件采购计划表</summary>
public class PurchasePlanDocument : ReportDocumentBase
{
    private readonly PurchaseReport1DataDto _data;

    public PurchasePlanDocument(PurchaseReport1DataDto data) => _data = data;

    public override string Title => "合同配套件采购计划表";
    public override string DocumentNo => DocNoPlan;

    protected override void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(8).Column(info =>
            {
                info.Item().Text($"合同名称：{_data.ContractName}").FontSize(9);
                info.Item().Text($"工作令号：{_data.ContractNo}").FontSize(9);
                info.Item().Text($"方案编号：{_data.Fabh}    计划编号：{_data.PlanNo}").FontSize(9);
            });

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(24);   // 序号
                    columns.RelativeColumn(2.2f); // 产品名称
                    columns.RelativeColumn(2.5f); // 规格型号
                    columns.ConstantColumn(36);   // 单位
                    columns.ConstantColumn(44);   // 数量
                    columns.ConstantColumn(56);   // 需要日期
                    columns.RelativeColumn(1.5f); // 生产厂
                    columns.ConstantColumn(48);   // 变更签字
                    columns.RelativeColumn(1.2f); // 备注
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("序号");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("产品名称");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("规格、型号、等级");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("单位");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("数量");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("需要日期");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("生产厂");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("变更签字");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("备注");
                });

                foreach (var row in _data.Rows)
                {
                    var strike = row.IsDeleted ? TextStyle.Default.Strikethrough() : TextStyle.Default;

                    table.Cell().Element(CellStyle).AlignCenter().Text(row.Seq.ToString()).Style(strike);
                    table.Cell().Element(CellStyle).Text(row.ItemName).Style(strike);
                    table.Cell().Element(CellStyle).Text(row.ItemSpec).Style(strike);
                    table.Cell().Element(CellStyle).AlignCenter().Text(row.ItemUnit).Style(strike);
                    table.Cell().Element(CellStyle).AlignRight().Text(FormatQty(row.ItemQty)).Style(strike);
                    table.Cell().Element(CellStyle).AlignCenter().Text(row.NeedDateText ?? string.Empty).Style(strike);
                    table.Cell().Element(CellStyle).Text(row.ItemManufacturer).Style(strike);
                    table.Cell().Element(CellStyle).Text(string.Empty);
                    table.Cell().Element(CellStyle).Text(row.Remark).Style(strike);
                }
            });

            col.Item().PaddingTop(10).Text(
                "注：1.一般配套件采购由实施单位编制、单位负责人审核，合同管理单位批准。" +
                "2.批量较大（＞10万元）的配套件采购由实施单位编制、单位负责人签署意见，合同管理单位审核，主管副总经理批准。" +
                "3.供货单位在合格供方名单中选择。")
                .FontSize(7).LineHeight(1.3f);
        });
    }

    private static string FormatQty(decimal qty) =>
        qty % 1 == 0 ? ((int)qty).ToString() : qty.ToString("0.##");
}
