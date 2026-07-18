using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

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
    ///     Tracks connection strings whose shared inbox schema has been created.
    /// </summary>
    private static readonly HashSet<string> InitializedConnectionStrings = new(StringComparer.Ordinal);

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
        return CreateInboxContext(connectionString, InboxStoreOptions);
    }

    /// <summary>
    ///     Creates a SQL Server-backed inbox database context for the supplied store options.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="storeOptions">The inbox store options.</param>
    /// <returns>The database context.</returns>
    internal static IntegrationInboxDbContext CreateInboxContext(
        string connectionString,
        EntityFrameworkCoreInboxStoreOptions storeOptions)
    {
        var builder = new DbContextOptionsBuilder<IntegrationInboxDbContext>()
            .UseSqlServer(CreateScopedConnectionString(connectionString, storeOptions));

        return new IntegrationInboxDbContext(builder.Options, storeOptions, EfCoreStorageProvider.SqlServer);
    }

    /// <summary>
    ///     Builds a connection string scoped to one inbox table so EF model caching stays isolated per test table.
    /// </summary>
    /// <param name="connectionString">The base SQL Server connection string.</param>
    /// <param name="storeOptions">The inbox store options.</param>
    /// <returns>The scoped connection string.</returns>
    internal static string CreateScopedConnectionString(
        string connectionString,
        EntityFrameworkCoreInboxStoreOptions storeOptions)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = $"litebus_ef_{storeOptions.SchemaName}_{storeOptions.TableName}"
        };

        return builder.ConnectionString;
    }

    /// <summary>
    ///     Creates the shared inbox schema and table once per connection string.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    private static async Task EnsureInboxSchemaOnceAsync(string connectionString)
    {
        if (InitializedConnectionStrings.Contains(connectionString))
        {
            return;
        }

        await InboxSchemaLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (InitializedConnectionStrings.Contains(connectionString))
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
            }

            InitializedConnectionStrings.Add(connectionString);
        }
        finally
        {
            InboxSchemaLock.Release();
        }
    }
}
