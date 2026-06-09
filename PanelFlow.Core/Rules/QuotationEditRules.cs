namespace PanelFlow.Core.Rules;

/// <summary>
/// 报价单「报价人本人可操作」规则，与 Index 行操作按钮、ValidateEditAccess、结构维护检索口径一致。
/// 含管理员例外的写操作（如结构维护 ApplyStructure）在各自服务中另行组合。
/// </summary>
public static class QuotationEditRules
{
    /// <summary>BJFAT.dqzt：已成立，不可编辑/删除/结构维护检索。</summary>
    public const int EstablishedStatus = 10;

    public static bool IsEstablished(int dqzt) => dqzt == EstablishedStatus;

    public static bool IsOwner(string quoter, string loginUserName)
    {
        if (string.IsNullOrWhiteSpace(loginUserName))
            return false;

        return string.Equals(
            (quoter ?? string.Empty).Trim(),
            loginUserName.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>本人且未成立：列表操作按钮、检索下拉、编辑/删除/改方案编号等前置条件。</summary>
    public static bool CanOwnerOperate(string quoter, int dqzt, string loginUserName) =>
        IsOwner(quoter, loginUserName) && !IsEstablished(dqzt);
}
