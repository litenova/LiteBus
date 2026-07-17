using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Creates and resets MySQL inbox stores for shared contract tests.
/// </summary>
internal static class EfCoreMySqlTestInfrastructure
{
    /// <summary>
    ///     The database used by the inbox provider tests.
    /// </summary>
    internal const string DatabaseName = "litebus_tests";

    /// <summary>
    ///     The table used by the inbox provider tests.
    /// </summary>
    internal const string TableName = "inbox_ef_mysql_contract_tests";

    /// <summary>
    ///     Serializes database creation for the current test process.
    /// </summary>
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);

    /// <summary>
    ///     Tracks servers whose inbox test database has been created.
    /// </summary>
    private static readonly HashSet<string> InitializedConnectionStrings = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the options used by MySQL inbox contract tests.
    /// </summary>
    internal static EntityFrameworkCoreInboxStoreOptions StoreOptions { get; } = new()
    {
        SchemaName = DatabaseName,
        TableName = TableName,
        LeaseProvider = EfCoreStorageProvider.MySql
    };

    /// <summary>
    ///     Creates a new context connected to the inbox test database.
    /// </summary>
    /// <param name="connectionString">The base MySQL connection string.</param>
    /// <returns>The configured inbox context.</returns>
    internal static IntegrationInboxDbContext CreateContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<IntegrationInboxDbContext>()
            .UseMySql(
                connectionString,
                ServerVersion.AutoDetect(connectionString));

        return new IntegrationInboxDbContext(builder.Options, StoreOptions, EfCoreStorageProvider.MySql);
    }

    /// <summary>
    ///     Creates the inbox database and table once, then removes rows before a contract test.
    /// </summary>
    /// <param name="connectionString">The base MySQL connection string.</param>
    internal static async Task ResetAsync(string connectionString)
    {
        await EnsureCreatedAsync(connectionString).ConfigureAwait(false);

        var context = CreateContext(connectionString);
        await using (context.ConfigureAwait(false))
        {
            await context.InboxMessages.ExecuteDeleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates the MySQL database and inbox table for a new server.
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
                    "DROP TABLE IF EXISTS `inbox_ef_mysql_contract_tests`;").ConfigureAwait(false);
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
