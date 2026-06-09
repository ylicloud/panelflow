namespace PanelFlow.Web.Models.Quotation;

public class QuotationTreeNodeViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>"cabinet"=有Level 2子行的控制柜；"leaf"=无子行的费用项（如运费）</summary>
    public string NodeType { get; set; } = "cabinet";
    public List<QuotationTreeLevel2NodeViewModel> Level2Children { get; set; } = [];
}

public class QuotationTreeLevel2NodeViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>该Level 2节点下是否有12位子行。数据驱动：true时点击切换Level 3元件表；false时高亮Level 2表对应行。</summary>
    public bool HasLevel3Children { get; set; }
}

public class QuotationAttrNodeViewModel
{
    /// <summary>Level 2属性的 x_lx 值（如13=壳体），用于属性视图树和GetAttributeItems查询</summary>
    public int Xlx { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>拥有该属性的控制柜数量</summary>
    public int Count { get; set; }
}
