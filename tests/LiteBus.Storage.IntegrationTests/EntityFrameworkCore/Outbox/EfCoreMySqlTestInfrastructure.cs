using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Creates and resets MySQL outbox stores for shared contract tests.
/// </summary>
internal static class EfCoreMySqlTestInfrastructure
{
    /// <summary>
    ///     The database used by the outbox provider tests.
    /// </summary>
    internal const string DatabaseName = "litebus_tests";

    /// <summary>
    ///     The table used by the outbox provider tests.
    /// </summary>
    internal const string TableName = "outbox_ef_mysql_contract_tests";

    /// <summary>
    ///     Serializes database creation for the current test process.
    /// </summary>
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);

    /// <summary>
    ///     Tracks servers whose outbox test database has been created.
    /// </summary>
    private static readonly HashSet<string> InitializedConnectionStrings = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the options used by MySQL outbox contract tests.
    /// </summary>
    internal static EntityFrameworkCoreOutboxStoreOptions StoreOptions { get; } = new()
    {
        SchemaName = DatabaseName,
        TableName = TableName,
        LeaseProvider = EfCoreStorageProvider.MySql
    };

    /// <summary>
    ///     Creates a new context connected to the outbox test database.
    /// </summary>
    /// <param name="connectionString">The base MySQL connection string.</param>
    /// <returns>The configured outbox context.</returns>
    internal static IntegrationOutboxDbContext CreateContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<IntegrationOutboxDbContext>()
            .UseMySql(
                connectionString,
                ServerVersion.AutoDetect(connectionString));

        return new IntegrationOutboxDbContext(builder.Options, StoreOptions, EfCoreStorageProvider.MySql);
    }

    /// <summary>
    ///     Creates the outbox database and table once, then removes rows before a contract test.
    /// </summary>
    /// <param name="connectionString">The base MySQL connection string.</param>
    internal static async Task ResetAsync(string connectionString)
    {
        await EnsureCreatedAsync(connectionString).ConfigureAwait(false);

        var context = CreateContext(connectionString);
        await using (context.ConfigureAwait(false))
        {
            await context.OutboxMessages.ExecuteDeleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates the MySQL database and outbox table for a new server.
    /// </summary>
    /// <param name="connectionString">The base MySQL connection string.</param>
    private static async Task EnsureCreatedAsync(string connectionString)
    {
        if (InitializedConnectionStrings.Contains(connectionString))
        {
            return;
        }

        await SchemaLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!InitializedConnectionStrings.Add(connectionString))
            {
                return;
            }

            var context = CreateContext(connectionString);
            await using (context.ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlRawAsync(
                    "DROP TABLE IF EXISTS `outbox_ef_mysql_contract_tests`;").ConfigureAwait(false);
                var databaseCreator = context.GetService<IRelationalDatabaseCreator>();
                await databaseCreator.CreateTablesAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            SchemaLock.Release();
        }
    }
}
