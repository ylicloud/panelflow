namespace PanelFlow.Infrastructure.Data;

/// <summary>
/// 历史库索引/存储过程要求的会话 SET 选项，须与后续 DML/EXEC 处于同一批次。
/// </summary>
public static class SqlServerLegacySession
{
    public const string OptionsSql = """
        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_WARNINGS ON;
        SET ARITHABORT ON;
        SET CONCAT_NULL_YIELDS_NULL ON;
        SET ANSI_PADDING ON;
        """;

    public static string PrefixBatch(string sql) => OptionsSql + Environment.NewLine + sql;
}
