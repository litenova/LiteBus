using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

internal static class PostgreSqlTestInfrastructure
{
    internal const string TestSchemaName = "litebus_tests";

    internal static readonly DateTimeOffset BaseTime = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    internal static PostgreSqlInboxStoreOptions CreateInboxOptions(string? tableName = null)
    {
        return new PostgreSqlInboxStoreOptions
        {
            SchemaName = TestSchemaName,
            TableName = tableName ?? $"inbox_{Guid.NewGuid():N}",
            ValidateSchemaCreationOnStartup = false
        };
    }

    internal static PostgreSqlOutboxStoreOptions CreateOutboxOptions(string? tableName = null)
    {
        return new PostgreSqlOutboxStoreOptions
        {
            SchemaName = TestSchemaName,
            TableName = tableName ?? $"outbox_{Guid.NewGuid():N}",
            ValidateSchemaCreationOnStartup = false
        };
    }

    internal static async Task EnsureInboxSchemaAsync(Npgsql.NpgsqlDataSource dataSource, PostgreSqlInboxStoreOptions options)
    {
        await PostgreSqlInboxSchema.EnsureAsync(dataSource, options);
    }

    internal static async Task EnsureOutboxSchemaAsync(Npgsql.NpgsqlDataSource dataSource, PostgreSqlOutboxStoreOptions options)
    {
        await PostgreSqlOutboxSchema.EnsureAsync(dataSource, options);
    }

    internal static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        return await condition().ConfigureAwait(false);
    }

}
