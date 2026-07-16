using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PanelFlow.Infrastructure.Data;

/// <summary>
/// 历史库表、索引视图与存储过程按 ANSI_NULLS ON / QUOTED_IDENTIFIER ON 创建，连接打开时统一会话选项。
/// </summary>
public sealed class SqlServerLegacySessionInterceptor : DbConnectionInterceptor
{
    private const string SessionOptionsSql = SqlServerLegacySession.OptionsSql;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplySessionOptions(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplySessionOptionsAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void ApplySessionOptions(DbConnection connection)
    {
        if (connection is not SqlConnection)
            return;

        using var cmd = connection.CreateCommand();
        cmd.CommandText = SessionOptionsSql;
        cmd.ExecuteNonQuery();
    }

    private static async Task ApplySessionOptionsAsync(DbConnection connection, CancellationToken ct)
    {
        if (connection is not SqlConnection)
            return;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = SessionOptionsSql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
