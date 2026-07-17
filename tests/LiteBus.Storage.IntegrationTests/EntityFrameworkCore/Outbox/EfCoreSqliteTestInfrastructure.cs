using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Creates and resets file-backed SQLite outbox stores for shared contract tests.
/// </summary>
internal static class EfCoreSqliteTestInfrastructure
{
    /// <summary>
    ///     Serializes database creation for the current test process.
    /// </summary>
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);

    /// <summary>
    ///     Tracks database files whose outbox table has been created.
    /// </summary>
    private static readonly HashSet<string> InitializedConnectionStrings = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the options used by SQLite outbox contract tests.
    /// </summary>
    internal static EntityFrameworkCoreOutboxStoreOptions StoreOptions { get; } = new()
    {
        SchemaName = "main",
        TableName = "outbox_ef_sqlite_contract_tests",
        LeaseProvider = EfCoreStorageProvider.Sqlite
    };

    /// <summary>
    ///     Creates a new context connected to the shared file-backed database.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <returns>The configured outbox context.</returns>
    internal static IntegrationOutboxDbContext CreateContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<IntegrationOutboxDbContext>()
            .UseSqlite(connectionString);

        return new IntegrationOutboxDbContext(builder.Options, StoreOptions, EfCoreStorageProvider.Sqlite);
    }

    /// <summary>
    ///     Creates the outbox table once and removes rows before a contract test.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
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
    ///     Creates the SQLite schema for a new database file.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
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
                await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            SchemaLock.Release();
        }
    }
}
