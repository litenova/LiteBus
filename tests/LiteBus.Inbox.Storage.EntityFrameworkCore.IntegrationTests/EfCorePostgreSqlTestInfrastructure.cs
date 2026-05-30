using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Inbox.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Shared PostgreSQL helpers for Entity Framework Core inbox integration tests.
/// </summary>
internal static class EfCorePostgreSqlTestInfrastructure
{
    /// <summary>
    ///     The PostgreSQL schema that holds contract test tables.
    /// </summary>
    internal const string SchemaName = "litebus_tests";

    /// <summary>
    ///     The inbox table used by all contract tests in this assembly.
    /// </summary>
    internal const string InboxTableName = "inbox_ef_contract_tests";

    /// <summary>
    ///     Synchronizes one-time schema creation across contract tests.
    /// </summary>
    private static readonly SemaphoreSlim InboxSchemaLock = new(1, 1);

    /// <summary>
    ///     Tracks whether the shared inbox table has been created.
    /// </summary>
    private static bool _inboxSchemaInitialized;

    /// <summary>
    ///     Gets the store options used by inbox contract tests.
    /// </summary>
    internal static EfCoreInboxStoreOptions InboxOptions { get; } = new()
    {
        SchemaName = SchemaName,
        TableName = InboxTableName
    };

    /// <summary>
    ///     Ensures the shared inbox table exists and clears rows before one contract test run.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    internal static async Task ResetInboxTableAsync(string connectionString)
    {
        await EnsureInboxSchemaOnceAsync(connectionString).ConfigureAwait(false);

        await using var context = CreateInboxContext(connectionString);
        await context.Database.ExecuteSqlRawAsync(
            $"""TRUNCATE TABLE "{SchemaName}"."{InboxTableName}";""").ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates a PostgreSQL-backed inbox database context for integration tests.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The database context.</returns>
    internal static IntegrationInboxDbContext CreateInboxContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<IntegrationInboxDbContext>()
            .UseNpgsql(connectionString);

        return new IntegrationInboxDbContext(builder.Options, InboxOptions);
    }

    /// <summary>
    ///     Creates the shared inbox schema and table once per test process.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    private static async Task EnsureInboxSchemaOnceAsync(string connectionString)
    {
        if (_inboxSchemaInitialized)
        {
            return;
        }

        await InboxSchemaLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_inboxSchemaInitialized)
            {
                return;
            }

            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await PostgreSqlInboxSchema.EnsureAsync(
                dataSource,
                new PostgreSqlInboxStoreOptions
                {
                    SchemaName = SchemaName,
                    TableName = InboxTableName,
                    ValidateSchemaCreationOnStartup = false
                }).ConfigureAwait(false);

            _inboxSchemaInitialized = true;
        }
        finally
        {
            InboxSchemaLock.Release();
        }
    }
}
