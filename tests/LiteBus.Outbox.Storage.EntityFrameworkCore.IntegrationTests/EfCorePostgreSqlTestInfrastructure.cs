using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Outbox.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Shared PostgreSQL helpers for Entity Framework Core outbox integration tests.
/// </summary>
internal static class EfCorePostgreSqlTestInfrastructure
{
    /// <summary>
    ///     The PostgreSQL schema that holds contract test tables.
    /// </summary>
    internal const string SchemaName = "litebus_tests";

    /// <summary>
    ///     The outbox table used by all contract tests in this assembly.
    /// </summary>
    internal const string OutboxTableName = "outbox_ef_contract_tests";

    /// <summary>
    ///     Synchronizes one-time schema creation across contract tests.
    /// </summary>
    private static readonly SemaphoreSlim OutboxSchemaLock = new(1, 1);

    /// <summary>
    ///     Tracks whether the shared outbox table has been created.
    /// </summary>
    private static bool _outboxSchemaInitialized;

    /// <summary>
    ///     Gets the store options used by outbox contract tests.
    /// </summary>
    internal static EfCoreOutboxStoreOptions OutboxOptions { get; } = new()
    {
        SchemaName = SchemaName,
        TableName = OutboxTableName
    };

    /// <summary>
    ///     Ensures the shared outbox table exists and clears rows before one contract test run.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    internal static async Task ResetOutboxTableAsync(string connectionString)
    {
        await EnsureOutboxSchemaOnceAsync(connectionString).ConfigureAwait(false);

        await using var context = CreateOutboxContext(connectionString);
        await context.Database.ExecuteSqlRawAsync(
            $"""TRUNCATE TABLE "{SchemaName}"."{OutboxTableName}";""").ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates a PostgreSQL-backed outbox database context for integration tests.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The database context.</returns>
    internal static IntegrationOutboxDbContext CreateOutboxContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<IntegrationOutboxDbContext>()
            .UseNpgsql(connectionString);

        return new IntegrationOutboxDbContext(builder.Options, OutboxOptions);
    }

    /// <summary>
    ///     Creates the shared outbox schema and table once per test process.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    private static async Task EnsureOutboxSchemaOnceAsync(string connectionString)
    {
        if (_outboxSchemaInitialized)
        {
            return;
        }

        await OutboxSchemaLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_outboxSchemaInitialized)
            {
                return;
            }

            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            await PostgreSqlOutboxSchema.EnsureAsync(
                dataSource,
                new PostgreSqlOutboxStoreOptions
                {
                    SchemaName = SchemaName,
                    TableName = OutboxTableName,
                    ValidateSchemaCreationOnStartup = false
                }).ConfigureAwait(false);

            _outboxSchemaInitialized = true;
        }
        finally
        {
            OutboxSchemaLock.Release();
        }
    }
}
