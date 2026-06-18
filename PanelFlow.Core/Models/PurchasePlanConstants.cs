namespace PanelFlow.Core.Models;

/// <summary>采购计划状态</summary>
public static class PurchasePlanStatus
{
    public const short Draft = 0;
    public const short Issued = 1;
    public const short Completed = 2;
}

/// <summary>采购计划明细变更类型</summary>
public static class PurchaseChangeType
{
    public const short Normal = 0;
    public const short Modified = 1;
    public const short Added = 2;
    public const short Deleted = 3;
}
