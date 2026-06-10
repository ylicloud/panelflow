using PanelFlow.Web.Controllers;

namespace PanelFlow.Web.Models.Quotation;

public class QuotationPriceViewModel
{
    public string QuotationNo { get; set; } = string.Empty;
    public string QuotationName { get; set; } = string.Empty;
    public int CurrentStatus { get; set; }
    public string ActiveSection { get; set; } = PriceSection.ImportComponents;
    public List<QuotationTreeNodeViewModel> TreeNodes { get; set; } = [];

    /// <summary>属性视图树节点列表（Level 2属性分组，按x_lx）</summary>
    public List<QuotationAttrNodeViewModel> AttrNodes { get; set; } = [];

    /// <summary>
    /// 当前用户是否有编辑权限（报价人本人或管理员）。
    /// 用于前端控制"引用历史报价"按钮和保存按钮的显示。
    /// </summary>
    public bool CanEdit { get; set; }

    /// <summary>
    /// 是否为只读详情页（Details 与 FillPrice 共用模板时设为 true）。
    /// </summary>
    public bool IsReadOnlyView { get; set; }
}
