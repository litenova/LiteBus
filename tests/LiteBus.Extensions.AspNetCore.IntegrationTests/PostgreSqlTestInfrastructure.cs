using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;

namespace LiteBus.Extensions.AspNetCore.IntegrationTests;

/// <summary>
///     Shared PostgreSQL helpers for management endpoint integration tests.
/// </summary>
internal static class PostgreSqlTestInfrastructure
{
    /// <summary>
    ///     The PostgreSQL schema that holds integration test tables.
    /// </summary>
    internal const string TestSchemaName = "litebus_tests";

    /// <summary>
    ///     Creates isolated inbox store options for one test run.
    /// </summary>
    /// <returns>The PostgreSQL inbox store options.</returns>
    internal static PostgreSqlInboxStoreOptions CreateInboxOptions()
    {
        return new PostgreSqlInboxStoreOptions
        {
            SchemaName = TestSchemaName,
            TableName = $"inbox_mgmt_{Guid.NewGuid():N}",
            ValidateSchemaCreationOnStartup = false
        };
    }

    /// <summary>
    ///     Creates isolated outbox store options for one test run.
    /// </summary>
    /// <returns>The PostgreSQL outbox store options.</returns>
    internal static PostgreSqlOutboxStoreOptions CreateOutboxOptions()
    {
        return new PostgreSqlOutboxStoreOptions
        {
            SchemaName = TestSchemaName,
            TableName = $"outbox_mgmt_{Guid.NewGuid():N}",
            ValidateSchemaCreationOnStartup = false
        };
    }

    /// <summary>
    ///     Ensures the inbox table exists for the supplied options.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The inbox store options.</param>
    internal static async Task EnsureInboxSchemaAsync(Npgsql.NpgsqlDataSource dataSource, PostgreSqlInboxStoreOptions options)
    {
        await PostgreSqlInboxSchema.EnsureAsync(dataSource, options);
    }

    /// <summary>
    ///     Ensures the outbox table exists for the supplied options.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The outbox store options.</param>
    internal static async Task EnsureOutboxSchemaAsync(Npgsql.NpgsqlDataSource dataSource, PostgreSqlOutboxStoreOptions options)
    {
        await PostgreSqlOutboxSchema.EnsureAsync(dataSource, options);
    }
}
