using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Shared SQL Server helpers for Entity Framework Core inbox integration tests.
/// </summary>
internal static class EfCoreSqlServerTestInfrastructure
{
    /// <summary>
    ///     The schema that holds contract test tables.
    /// </summary>
    internal const string SchemaName = "litebus_tests";

    /// <summary>
    ///     The inbox table used by SQL Server contract tests.
    /// </summary>
    internal const string InboxTableName = "inbox_ef_sqlserver_contract_tests";

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
    internal static EntityFrameworkCoreInboxStoreOptions InboxStoreOptions { get; } = new()
    {
        SchemaName = SchemaName,
        TableName = InboxTableName,
        LeaseProvider = EfCoreStorageProvider.SqlServer
    };

    /// <summary>
    ///     Ensures the shared inbox table exists and clears rows before one contract test run.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    internal static async Task ResetInboxTableAsync(string connectionString)
    {
        await EnsureInboxSchemaOnceAsync(connectionString).ConfigureAwait(false);

         var context = CreateInboxContext(connectionString);
         await using (context.ConfigureAwait(false))
         {

        await context.Database.ExecuteSqlRawAsync(
            $"""DELETE FROM [{SchemaName}].[{InboxTableName}];""").ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates a SQL Server-backed inbox database context for integration tests.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <returns>The database context.</returns>
    internal static IntegrationInboxDbContext CreateInboxContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<IntegrationInboxDbContext>()
            .UseSqlServer(connectionString);

        return new IntegrationInboxDbContext(builder.Options, InboxStoreOptions, EfCoreStorageProvider.SqlServer);
    }

    /// <summary>
    ///     Creates the shared inbox schema and table once per test process.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
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

             var context = CreateInboxContext(connectionString);
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
            _inboxSchemaInitialized = true;
            }
        }
        finally
        {
            InboxSchemaLock.Release();
        }
    }
}
