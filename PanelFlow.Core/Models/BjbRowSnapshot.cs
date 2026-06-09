namespace PanelFlow.Core.Models;

/// <summary>
/// BJB 行计价与扩展字段快照，结构重编码写回时原样保留。
/// </summary>
public class BjbRowSnapshot
{
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
    public int Xcgf { get; set; } = 1;
}
