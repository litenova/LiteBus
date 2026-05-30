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

    internal sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset initial)
        {
            _utcNow = initial;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow += amount;
    }
}
