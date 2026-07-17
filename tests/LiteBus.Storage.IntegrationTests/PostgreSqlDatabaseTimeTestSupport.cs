using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests;

internal static class PostgreSqlDatabaseTimeTestSupport
{
    internal static Task ExpireLeaseAsync(
        NpgsqlDataSource dataSource,
        string schemaName,
        string tableName,
        Guid messageId)
    {
        return SetDeadlineInPastAsync(dataSource, schemaName, tableName, "lease_expires_at", messageId);
    }

    internal static async Task ExpireLeaseAsync(
        string connectionString,
        string schemaName,
        string tableName,
        Guid messageId)
    {
        var dataSource = NpgsqlDataSource.Create(connectionString);
        await using (dataSource.ConfigureAwait(false))
        {
            await ExpireLeaseAsync(dataSource, schemaName, tableName, messageId).ConfigureAwait(false);
        }
    }

    internal static Task MakeVisibleAsync(
        NpgsqlDataSource dataSource,
        string schemaName,
        string tableName,
        Guid messageId)
    {
        return SetDeadlineInPastAsync(dataSource, schemaName, tableName, "visible_after", messageId);
    }

    internal static async Task MakeVisibleAsync(
        string connectionString,
        string schemaName,
        string tableName,
        Guid messageId)
    {
        var dataSource = NpgsqlDataSource.Create(connectionString);
        await using (dataSource.ConfigureAwait(false))
        {
            await MakeVisibleAsync(dataSource, schemaName, tableName, messageId).ConfigureAwait(false);
        }
    }

    private static async Task SetDeadlineInPastAsync(
        NpgsqlDataSource dataSource,
        string schemaName,
        string tableName,
        string columnName,
        Guid messageId)
    {
        var qualifiedTableName = PostgreSqlIdentifier.Qualify(schemaName, tableName);
        var quotedColumnName = PostgreSqlIdentifier.Quote(columnName);

        var command = dataSource.CreateCommand($$"""
            UPDATE {{qualifiedTableName}}
            SET {{quotedColumnName}} = CURRENT_TIMESTAMP - INTERVAL '1 second'
            WHERE "message_id" = @messageId;
            """);
        await using (command.ConfigureAwait(false))
        {
            command.Parameters.AddWithValue("messageId", messageId);

            var affectedRows = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            affectedRows.Should().Be(1);
        }
    }
}
