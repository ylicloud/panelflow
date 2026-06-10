namespace PanelFlow.Core.Models;

public class QuotationStructureDto
{
    public string Fabh { get; set; } = string.Empty;
    public string QuotationName { get; set; } = string.Empty;
    public string Quoter { get; set; } = string.Empty;
    public int CurrentStatus { get; set; }
    public bool CanEdit { get; set; }
    public int OrphanCount { get; set; }
    public List<QuotationStructureTreeNodeDto> Tree { get; set; } = [];
}

public class QuotationStructureTreeNodeDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Xlx { get; set; }
    public int Level { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsLocked { get; set; }
    public List<QuotationStructureTreeNodeDto> Children { get; set; } = [];
}
