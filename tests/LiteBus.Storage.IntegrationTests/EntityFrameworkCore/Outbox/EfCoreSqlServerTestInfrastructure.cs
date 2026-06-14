using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Shared SQL Server helpers for Entity Framework Core outbox integration tests.
/// </summary>
internal static class EfCoreSqlServerTestInfrastructure
{
    /// <summary>
    ///     The schema that holds contract test tables.
    /// </summary>
    internal const string SchemaName = "litebus_tests";

    /// <summary>
    ///     The outbox table used by SQL Server contract tests.
    /// </summary>
    internal const string OutboxTableName = "outbox_ef_sqlserver_contract_tests";

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
    internal static EntityFrameworkCoreOutboxStoreOptions OutboxStoreOptions { get; } = new()
    {
        SchemaName = SchemaName,
        TableName = OutboxTableName,
        LeaseProvider = EfCoreStorageProvider.SqlServer
    };

    /// <summary>
    ///     Ensures the shared outbox table exists and clears rows before one contract test run.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    internal static async Task ResetOutboxTableAsync(string connectionString)
    {
        await EnsureOutboxSchemaOnceAsync(connectionString).ConfigureAwait(false);

         var context = CreateOutboxContext(connectionString);
         await using (context.ConfigureAwait(false))
         {

        await context.Database.ExecuteSqlRawAsync(
            $"""DELETE FROM [{SchemaName}].[{OutboxTableName}];""").ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates a SQL Server-backed outbox database context for integration tests.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <returns>The database context.</returns>
    internal static IntegrationOutboxDbContext CreateOutboxContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<IntegrationOutboxDbContext>()
            .UseSqlServer(connectionString);

        return new IntegrationOutboxDbContext(builder.Options, OutboxStoreOptions, EfCoreStorageProvider.SqlServer);
    }

    /// <summary>
    ///     Creates the shared outbox schema and table once per test process.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
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

             var context = CreateOutboxContext(connectionString);
             await using (context.ConfigureAwait(false))
             {

            await context.Database.ExecuteSqlRawAsync(
                $"""
                 IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{SchemaName}')
                 BEGIN
                     EXEC(N'CREATE SCHEMA [{SchemaName}]');
                 END
                 """).ConfigureAwait(false);

            await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
            _outboxSchemaInitialized = true;
            }
        }
        finally
        {
            OutboxSchemaLock.Release();
        }
    }
}
