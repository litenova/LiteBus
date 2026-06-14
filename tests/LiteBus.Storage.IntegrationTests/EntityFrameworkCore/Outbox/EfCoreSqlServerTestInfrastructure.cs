using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
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
    ///     Tracks connection strings whose shared outbox schema has been created.
    /// </summary>
    private static readonly HashSet<string> InitializedConnectionStrings = new(StringComparer.Ordinal);

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
        return CreateOutboxContext(connectionString, OutboxStoreOptions);
    }

    /// <summary>
    ///     Creates a SQL Server-backed outbox database context for the supplied store options.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <returns>The database context.</returns>
    internal static IntegrationOutboxDbContext CreateOutboxContext(
        string connectionString,
        EntityFrameworkCoreOutboxStoreOptions storeOptions)
    {
        var builder = new DbContextOptionsBuilder<IntegrationOutboxDbContext>()
            .UseSqlServer(CreateScopedConnectionString(connectionString, storeOptions));

        return new IntegrationOutboxDbContext(builder.Options, storeOptions, EfCoreStorageProvider.SqlServer);
    }

    /// <summary>
    ///     Builds a connection string scoped to one outbox table so EF model caching stays isolated per test table.
    /// </summary>
    /// <param name="connectionString">The base SQL Server connection string.</param>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <returns>The scoped connection string.</returns>
    internal static string CreateScopedConnectionString(
        string connectionString,
        EntityFrameworkCoreOutboxStoreOptions storeOptions)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = $"litebus_ef_{storeOptions.SchemaName}_{storeOptions.TableName}"
        };

        return builder.ConnectionString;
    }

    /// <summary>
    ///     Creates the shared outbox schema and table once per connection string.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    private static async Task EnsureOutboxSchemaOnceAsync(string connectionString)
    {
        if (InitializedConnectionStrings.Contains(connectionString))
        {
            return;
        }

        await OutboxSchemaLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (InitializedConnectionStrings.Contains(connectionString))
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
            }

            InitializedConnectionStrings.Add(connectionString);
        }
        finally
        {
            OutboxSchemaLock.Release();
        }
    }
}
