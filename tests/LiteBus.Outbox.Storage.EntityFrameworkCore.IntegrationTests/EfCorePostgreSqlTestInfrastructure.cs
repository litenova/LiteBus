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
    ///     Tracks connection strings whose shared outbox schema has been created.
    /// </summary>
    private static readonly HashSet<string> InitializedConnectionStrings = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the store options used by outbox contract tests.
    /// </summary>
    internal static EntityFrameworkCoreOutboxStoreOptions OutboxStoreOptions { get; } = new()
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

         var context = CreateOutboxContext(connectionString);
         await using (context.ConfigureAwait(false))
         {

        await context.Database.ExecuteSqlRawAsync(
            $"""TRUNCATE TABLE "{SchemaName}"."{OutboxTableName}";""").ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates a PostgreSQL-backed outbox database context for integration tests.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The database context.</returns>
    internal static IntegrationOutboxDbContext CreateOutboxContext(string connectionString)
    {
        return CreateOutboxContext(connectionString, OutboxStoreOptions);
    }

    /// <summary>
    ///     Creates a PostgreSQL-backed outbox database context for the supplied store options.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <returns>The database context.</returns>
    internal static IntegrationOutboxDbContext CreateOutboxContext(
        string connectionString,
        EntityFrameworkCoreOutboxStoreOptions storeOptions)
    {
        var builder = new DbContextOptionsBuilder<IntegrationOutboxDbContext>()
            .UseNpgsql(CreateScopedConnectionString(connectionString, storeOptions));

        return new IntegrationOutboxDbContext(builder.Options, storeOptions);
    }

    /// <summary>
    ///     Builds a connection string scoped to one outbox table so EF model caching stays isolated per test table.
    /// </summary>
    /// <param name="connectionString">The base PostgreSQL connection string.</param>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <returns>The scoped connection string.</returns>
    internal static string CreateScopedConnectionString(
        string connectionString,
        EntityFrameworkCoreOutboxStoreOptions storeOptions)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = $"litebus_ef_{storeOptions.SchemaName}_{storeOptions.TableName}"
        };

        return builder.ConnectionString;
    }

    /// <summary>
    ///     Creates the shared outbox schema and table once per test process.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    private static async Task EnsureOutboxSchemaOnceAsync(string connectionString)
    {
        if (InitializedConnectionStrings.Contains(connectionString))
        {
            return;
        }

        await OutboxSchemaLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (!InitializedConnectionStrings.Add(connectionString))
            {
                return;
            }

             var dataSource = NpgsqlDataSource.Create(connectionString);
             await using (dataSource.ConfigureAwait(false))
             {

            await PostgreSqlOutboxSchema.EnsureAsync(
                dataSource,
                new PostgreSqlOutboxStoreOptions
                {
                    SchemaName = SchemaName,
                    TableName = OutboxTableName,
                    ValidateSchemaCreationOnStartup = false
                }).ConfigureAwait(false);
            }
        }
        finally
        {
            OutboxSchemaLock.Release();
        }
    }
}
