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
    ///     Tracks connection strings whose shared inbox schema has been created.
    /// </summary>
    private static readonly HashSet<string> InitializedConnectionStrings = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the store options used by inbox contract tests.
    /// </summary>
    internal static EntityFrameworkCoreInboxStoreOptions InboxStoreOptions { get; } = new()
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

         var context = CreateInboxContext(connectionString);
         await using (context.ConfigureAwait(false))
         {

        await context.Database.ExecuteSqlRawAsync(
            $"""TRUNCATE TABLE "{SchemaName}"."{InboxTableName}";""").ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates a PostgreSQL-backed inbox database context for integration tests.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The database context.</returns>
    internal static IntegrationInboxDbContext CreateInboxContext(string connectionString)
    {
        return CreateInboxContext(connectionString, InboxStoreOptions);
    }

    /// <summary>
    ///     Creates a PostgreSQL-backed inbox database context for the supplied store options.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="storeOptions">The inbox store options.</param>
    /// <returns>The database context.</returns>
    internal static IntegrationInboxDbContext CreateInboxContext(
        string connectionString,
        EntityFrameworkCoreInboxStoreOptions storeOptions)
    {
        var builder = new DbContextOptionsBuilder<IntegrationInboxDbContext>()
            .UseNpgsql(CreateScopedConnectionString(connectionString, storeOptions));

        return new IntegrationInboxDbContext(builder.Options, storeOptions);
    }

    /// <summary>
    ///     Builds a connection string scoped to one inbox table so EF model caching stays isolated per test table.
    /// </summary>
    /// <param name="connectionString">The base PostgreSQL connection string.</param>
    /// <param name="storeOptions">The inbox store options.</param>
    /// <returns>The scoped connection string.</returns>
    internal static string CreateScopedConnectionString(
        string connectionString,
        EntityFrameworkCoreInboxStoreOptions storeOptions)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = $"litebus_ef_{storeOptions.SchemaName}_{storeOptions.TableName}"
        };

        return builder.ConnectionString;
    }

    /// <summary>
    ///     Creates the shared inbox schema and table once per test process.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    private static async Task EnsureInboxSchemaOnceAsync(string connectionString)
    {
        if (InitializedConnectionStrings.Contains(connectionString))
        {
            return;
        }

        await InboxSchemaLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (!InitializedConnectionStrings.Add(connectionString))
            {
                return;
            }

             var dataSource = NpgsqlDataSource.Create(connectionString);
             await using (dataSource.ConfigureAwait(false))
             {

            await PostgreSqlInboxSchema.EnsureAsync(
                dataSource,
                new PostgreSqlInboxStoreOptions
                {
                    SchemaName = SchemaName,
                    TableName = InboxTableName,
                    ValidateSchemaCreationOnStartup = false
                }).ConfigureAwait(false);
            }
        }
        finally
        {
            InboxSchemaLock.Release();
        }
    }
}
