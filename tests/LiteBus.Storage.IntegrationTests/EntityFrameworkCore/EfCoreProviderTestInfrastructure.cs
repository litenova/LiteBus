using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.EntityFrameworkCore;
using LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;
using LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Creates and resets Entity Framework Core inbox and outbox stores for shared contract tests, parameterized by provider and axis.
/// </summary>
internal static class EfCoreProviderTestInfrastructure
{
    /// <summary>
    ///     The schema that holds contract test tables on server providers.
    /// </summary>
    internal const string SchemaName = "litebus_tests";

    /// <summary>
    ///     Serializes one-time schema creation for the current test process.
    /// </summary>
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);

    /// <summary>
    ///     Tracks the provider, axis, and connection strings whose contract schema has been created.
    /// </summary>
    private static readonly HashSet<string> InitializedStores = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the inbox store options used by contract tests on one provider.
    /// </summary>
    /// <param name="provider">The database provider backing the store.</param>
    /// <returns>The store options for the provider.</returns>
    internal static EntityFrameworkCoreInboxStoreOptions InboxStoreOptions(EfCoreStorageProvider provider)
    {
        return provider switch
        {
            EfCoreStorageProvider.Sqlite => new EntityFrameworkCoreInboxStoreOptions
            {
                SchemaName = "main",
                TableName = "inbox_ef_sqlite_contract_tests",
                LeaseProvider = EfCoreStorageProvider.Sqlite
            },
            EfCoreStorageProvider.PostgreSql => new EntityFrameworkCoreInboxStoreOptions
            {
                SchemaName = SchemaName,
                TableName = "inbox_ef_contract_tests"
            },
            EfCoreStorageProvider.MySql => new EntityFrameworkCoreInboxStoreOptions
            {
                SchemaName = SchemaName,
                TableName = "inbox_ef_mysql_contract_tests",
                LeaseProvider = EfCoreStorageProvider.MySql
            },
            EfCoreStorageProvider.SqlServer => new EntityFrameworkCoreInboxStoreOptions
            {
                SchemaName = SchemaName,
                TableName = "inbox_ef_sqlserver_contract_tests",
                LeaseProvider = EfCoreStorageProvider.SqlServer
            },
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported Entity Framework Core test provider.")
        };
    }

    /// <summary>
    ///     Gets the outbox store options used by contract tests on one provider.
    /// </summary>
    /// <param name="provider">The database provider backing the store.</param>
    /// <returns>The store options for the provider.</returns>
    internal static EntityFrameworkCoreOutboxStoreOptions OutboxStoreOptions(EfCoreStorageProvider provider)
    {
        return provider switch
        {
            EfCoreStorageProvider.Sqlite => new EntityFrameworkCoreOutboxStoreOptions
            {
                SchemaName = "main",
                TableName = "outbox_ef_sqlite_contract_tests",
                LeaseProvider = EfCoreStorageProvider.Sqlite
            },
            EfCoreStorageProvider.PostgreSql => new EntityFrameworkCoreOutboxStoreOptions
            {
                SchemaName = SchemaName,
                TableName = "outbox_ef_contract_tests"
            },
            EfCoreStorageProvider.MySql => new EntityFrameworkCoreOutboxStoreOptions
            {
                SchemaName = SchemaName,
                TableName = "outbox_ef_mysql_contract_tests",
                LeaseProvider = EfCoreStorageProvider.MySql
            },
            EfCoreStorageProvider.SqlServer => new EntityFrameworkCoreOutboxStoreOptions
            {
                SchemaName = SchemaName,
                TableName = "outbox_ef_sqlserver_contract_tests",
                LeaseProvider = EfCoreStorageProvider.SqlServer
            },
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported Entity Framework Core test provider.")
        };
    }

    /// <summary>
    ///     Creates a new inbox context connected to the contract test store on one provider.
    /// </summary>
    /// <param name="provider">The database provider backing the store.</param>
    /// <param name="connectionString">The provider connection string.</param>
    /// <returns>The configured inbox context.</returns>
    internal static IntegrationInboxDbContext CreateInboxContext(EfCoreStorageProvider provider, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var storeOptions = InboxStoreOptions(provider);

        return provider switch
        {
            EfCoreStorageProvider.Sqlite => new IntegrationInboxDbContext(
                new DbContextOptionsBuilder<IntegrationInboxDbContext>().UseSqlite(connectionString).Options,
                storeOptions,
                EfCoreStorageProvider.Sqlite),
            EfCoreStorageProvider.PostgreSql => new IntegrationInboxDbContext(
                new DbContextOptionsBuilder<IntegrationInboxDbContext>()
                    .UseNpgsql(CreateInboxScopedConnectionString(connectionString, storeOptions)).Options,
                storeOptions),
            EfCoreStorageProvider.MySql => new IntegrationInboxDbContext(
                new DbContextOptionsBuilder<IntegrationInboxDbContext>()
                    .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).Options,
                storeOptions,
                EfCoreStorageProvider.MySql),
            EfCoreStorageProvider.SqlServer => new IntegrationInboxDbContext(
                new DbContextOptionsBuilder<IntegrationInboxDbContext>()
                    .UseSqlServer(CreateInboxSqlServerConnectionString(connectionString, storeOptions)).Options,
                storeOptions,
                EfCoreStorageProvider.SqlServer),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported Entity Framework Core test provider.")
        };
    }

    /// <summary>
    ///     Creates a new outbox context connected to the contract test store on one provider.
    /// </summary>
    /// <param name="provider">The database provider backing the store.</param>
    /// <param name="connectionString">The provider connection string.</param>
    /// <returns>The configured outbox context.</returns>
    internal static IntegrationOutboxDbContext CreateOutboxContext(EfCoreStorageProvider provider, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var storeOptions = OutboxStoreOptions(provider);

        return provider switch
        {
            EfCoreStorageProvider.Sqlite => new IntegrationOutboxDbContext(
                new DbContextOptionsBuilder<IntegrationOutboxDbContext>().UseSqlite(connectionString).Options,
                storeOptions,
                EfCoreStorageProvider.Sqlite),
            EfCoreStorageProvider.PostgreSql => new IntegrationOutboxDbContext(
                new DbContextOptionsBuilder<IntegrationOutboxDbContext>()
                    .UseNpgsql(CreateOutboxScopedConnectionString(connectionString, storeOptions)).Options,
                storeOptions),
            EfCoreStorageProvider.MySql => new IntegrationOutboxDbContext(
                new DbContextOptionsBuilder<IntegrationOutboxDbContext>()
                    .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).Options,
                storeOptions,
                EfCoreStorageProvider.MySql),
            EfCoreStorageProvider.SqlServer => new IntegrationOutboxDbContext(
                new DbContextOptionsBuilder<IntegrationOutboxDbContext>()
                    .UseSqlServer(CreateOutboxSqlServerConnectionString(connectionString, storeOptions)).Options,
                storeOptions,
                EfCoreStorageProvider.SqlServer),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported Entity Framework Core test provider.")
        };
    }

    /// <summary>
    ///     Ensures the shared inbox table exists and clears rows before one contract test run.
    /// </summary>
    /// <param name="provider">The database provider backing the store.</param>
    /// <param name="connectionString">The provider connection string.</param>
    internal static async Task ResetInboxTableAsync(EfCoreStorageProvider provider, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        switch (provider)
        {
            case EfCoreStorageProvider.Sqlite:
                await ResetInboxSqliteAsync(connectionString).ConfigureAwait(false);
                break;
            case EfCoreStorageProvider.PostgreSql:
                await ResetInboxPostgreSqlAsync(connectionString).ConfigureAwait(false);
                break;
            case EfCoreStorageProvider.MySql:
                await ResetInboxMySqlAsync(connectionString).ConfigureAwait(false);
                break;
            case EfCoreStorageProvider.SqlServer:
                await ResetInboxSqlServerAsync(connectionString).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported Entity Framework Core test provider.");
        }
    }

    /// <summary>
    ///     Ensures the shared outbox table exists and clears rows before one contract test run.
    /// </summary>
    /// <param name="provider">The database provider backing the store.</param>
    /// <param name="connectionString">The provider connection string.</param>
    internal static async Task ResetOutboxTableAsync(EfCoreStorageProvider provider, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        switch (provider)
        {
            case EfCoreStorageProvider.Sqlite:
                await ResetOutboxSqliteAsync(connectionString).ConfigureAwait(false);
                break;
            case EfCoreStorageProvider.PostgreSql:
                await ResetOutboxPostgreSqlAsync(connectionString).ConfigureAwait(false);
                break;
            case EfCoreStorageProvider.MySql:
                await ResetOutboxMySqlAsync(connectionString).ConfigureAwait(false);
                break;
            case EfCoreStorageProvider.SqlServer:
                await ResetOutboxSqlServerAsync(connectionString).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported Entity Framework Core test provider.");
        }
    }

    /// <summary>
    ///     Builds a PostgreSQL connection string scoped to one inbox table so EF model caching stays isolated per test table.
    /// </summary>
    /// <param name="connectionString">The base PostgreSQL connection string.</param>
    /// <param name="storeOptions">The inbox store options.</param>
    /// <returns>The scoped connection string.</returns>
    internal static string CreateInboxScopedConnectionString(
        string connectionString,
        EntityFrameworkCoreInboxStoreOptions storeOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(storeOptions);

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = $"litebus_ef_{storeOptions.SchemaName}_{storeOptions.TableName}"
        };

        return builder.ConnectionString;
    }

    /// <summary>
    ///     Builds a PostgreSQL connection string scoped to one outbox table so EF model caching stays isolated per test table.
    /// </summary>
    /// <param name="connectionString">The base PostgreSQL connection string.</param>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <returns>The scoped connection string.</returns>
    internal static string CreateOutboxScopedConnectionString(
        string connectionString,
        EntityFrameworkCoreOutboxStoreOptions storeOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(storeOptions);

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = $"litebus_ef_{storeOptions.SchemaName}_{storeOptions.TableName}"
        };

        return builder.ConnectionString;
    }

    /// <summary>
    ///     Creates the SQLite inbox table once and removes rows before a contract test.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    private static async Task ResetInboxSqliteAsync(string connectionString)
    {
        await EnsureOnceAsync(StoreKey("inbox", EfCoreStorageProvider.Sqlite, connectionString), async () =>
        {
            var context = CreateInboxContext(EfCoreStorageProvider.Sqlite, connectionString);
            await using (context.ConfigureAwait(false))
            {
                await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        var resetContext = CreateInboxContext(EfCoreStorageProvider.Sqlite, connectionString);
        await using (resetContext.ConfigureAwait(false))
        {
            await resetContext.InboxMessages.ExecuteDeleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates the SQLite outbox table once and removes rows before a contract test.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    private static async Task ResetOutboxSqliteAsync(string connectionString)
    {
        await EnsureOnceAsync(StoreKey("outbox", EfCoreStorageProvider.Sqlite, connectionString), async () =>
        {
            var context = CreateOutboxContext(EfCoreStorageProvider.Sqlite, connectionString);
            await using (context.ConfigureAwait(false))
            {
                await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        var resetContext = CreateOutboxContext(EfCoreStorageProvider.Sqlite, connectionString);
        await using (resetContext.ConfigureAwait(false))
        {
            await resetContext.OutboxMessages.ExecuteDeleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates the shared PostgreSQL inbox table once and clears rows before a contract test.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    private static async Task ResetInboxPostgreSqlAsync(string connectionString)
    {
        var storeOptions = InboxStoreOptions(EfCoreStorageProvider.PostgreSql);

        await EnsureOnceAsync(StoreKey("inbox", EfCoreStorageProvider.PostgreSql, connectionString), async () =>
        {
            var dataSource = NpgsqlDataSource.Create(connectionString);
            await using (dataSource.ConfigureAwait(false))
            {
                await PostgreSqlInboxSchema.EnsureAsync(
                    dataSource,
                    new PostgreSqlInboxStoreOptions
                    {
                        SchemaName = storeOptions.SchemaName,
                        TableName = storeOptions.TableName,
                        ValidateSchemaCreationOnStartup = false
                    }).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        var resetContext = CreateInboxContext(EfCoreStorageProvider.PostgreSql, connectionString);
        await using (resetContext.ConfigureAwait(false))
        {
            await resetContext.Database.ExecuteSqlRawAsync(
                TestSql($"""TRUNCATE TABLE "{storeOptions.SchemaName}"."{storeOptions.TableName}";""")).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates the shared PostgreSQL outbox table once and clears rows before a contract test.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    private static async Task ResetOutboxPostgreSqlAsync(string connectionString)
    {
        var storeOptions = OutboxStoreOptions(EfCoreStorageProvider.PostgreSql);

        await EnsureOnceAsync(StoreKey("outbox", EfCoreStorageProvider.PostgreSql, connectionString), async () =>
        {
            var dataSource = NpgsqlDataSource.Create(connectionString);
            await using (dataSource.ConfigureAwait(false))
            {
                await PostgreSqlOutboxSchema.EnsureAsync(
                    dataSource,
                    new PostgreSqlOutboxStoreOptions
                    {
                        SchemaName = storeOptions.SchemaName,
                        TableName = storeOptions.TableName,
                        ValidateSchemaCreationOnStartup = false
                    }).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        var resetContext = CreateOutboxContext(EfCoreStorageProvider.PostgreSql, connectionString);
        await using (resetContext.ConfigureAwait(false))
        {
            await resetContext.Database.ExecuteSqlRawAsync(
                TestSql($"""TRUNCATE TABLE "{storeOptions.SchemaName}"."{storeOptions.TableName}";""")).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates the MySQL inbox database and table once, then removes rows before a contract test.
    /// </summary>
    /// <param name="connectionString">The base MySQL connection string.</param>
    private static async Task ResetInboxMySqlAsync(string connectionString)
    {
        var storeOptions = InboxStoreOptions(EfCoreStorageProvider.MySql);

        await EnsureOnceAsync(StoreKey("inbox", EfCoreStorageProvider.MySql, connectionString), async () =>
        {
            var context = CreateInboxContext(EfCoreStorageProvider.MySql, connectionString);
            await using (context.ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlRawAsync(
                    TestSql($"DROP TABLE IF EXISTS `{storeOptions.TableName}`;")).ConfigureAwait(false);
                var databaseCreator = context.GetService<IRelationalDatabaseCreator>();
                await databaseCreator.CreateTablesAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        var resetContext = CreateInboxContext(EfCoreStorageProvider.MySql, connectionString);
        await using (resetContext.ConfigureAwait(false))
        {
            await resetContext.InboxMessages.ExecuteDeleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates the MySQL outbox database and table once, then removes rows before a contract test.
    /// </summary>
    /// <param name="connectionString">The base MySQL connection string.</param>
    private static async Task ResetOutboxMySqlAsync(string connectionString)
    {
        var storeOptions = OutboxStoreOptions(EfCoreStorageProvider.MySql);

        await EnsureOnceAsync(StoreKey("outbox", EfCoreStorageProvider.MySql, connectionString), async () =>
        {
            var context = CreateOutboxContext(EfCoreStorageProvider.MySql, connectionString);
            await using (context.ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlRawAsync(
                    TestSql($"DROP TABLE IF EXISTS `{storeOptions.TableName}`;")).ConfigureAwait(false);
                var databaseCreator = context.GetService<IRelationalDatabaseCreator>();
                await databaseCreator.CreateTablesAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        var resetContext = CreateOutboxContext(EfCoreStorageProvider.MySql, connectionString);
        await using (resetContext.ConfigureAwait(false))
        {
            await resetContext.OutboxMessages.ExecuteDeleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates the shared SQL Server inbox table once and clears rows before a contract test.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    private static async Task ResetInboxSqlServerAsync(string connectionString)
    {
        var storeOptions = InboxStoreOptions(EfCoreStorageProvider.SqlServer);

        await EnsureOnceAsync(StoreKey("inbox", EfCoreStorageProvider.SqlServer, connectionString), async () =>
        {
            var context = CreateInboxContext(EfCoreStorageProvider.SqlServer, connectionString);
            await using (context.ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlRawAsync(
                    TestSql($"""
                     IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{storeOptions.SchemaName}')
                     BEGIN
                         EXEC(N'CREATE SCHEMA [{storeOptions.SchemaName}]');
                     END
                     """)).ConfigureAwait(false);

                await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        var resetContext = CreateInboxContext(EfCoreStorageProvider.SqlServer, connectionString);
        await using (resetContext.ConfigureAwait(false))
        {
            await resetContext.Database.ExecuteSqlRawAsync(
                TestSql($"""DELETE FROM [{storeOptions.SchemaName}].[{storeOptions.TableName}];""")).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates the shared SQL Server outbox table once and clears rows before a contract test.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    private static async Task ResetOutboxSqlServerAsync(string connectionString)
    {
        var storeOptions = OutboxStoreOptions(EfCoreStorageProvider.SqlServer);

        await EnsureOnceAsync(StoreKey("outbox", EfCoreStorageProvider.SqlServer, connectionString), async () =>
        {
            var context = CreateOutboxContext(EfCoreStorageProvider.SqlServer, connectionString);
            await using (context.ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlRawAsync(
                    TestSql($"""
                     IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{storeOptions.SchemaName}')
                     BEGIN
                         EXEC(N'CREATE SCHEMA [{storeOptions.SchemaName}]');
                     END
                     """)).ConfigureAwait(false);

                await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        var resetContext = CreateOutboxContext(EfCoreStorageProvider.SqlServer, connectionString);
        await using (resetContext.ConfigureAwait(false))
        {
            await resetContext.Database.ExecuteSqlRawAsync(
                TestSql($"""DELETE FROM [{storeOptions.SchemaName}].[{storeOptions.TableName}];""")).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Builds a SQL Server connection string scoped to one inbox table so EF model caching stays isolated per test table.
    /// </summary>
    /// <param name="connectionString">The base SQL Server connection string.</param>
    /// <param name="storeOptions">The inbox store options.</param>
    /// <returns>The scoped connection string.</returns>
    private static string CreateInboxSqlServerConnectionString(
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
    ///     Builds a SQL Server connection string scoped to one outbox table so EF model caching stays isolated per test table.
    /// </summary>
    /// <param name="connectionString">The base SQL Server connection string.</param>
    /// <param name="storeOptions">The outbox store options.</param>
    /// <returns>The scoped connection string.</returns>
    private static string CreateOutboxSqlServerConnectionString(
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
    ///     Runs one-time schema creation for a provider, axis, and connection string.
    /// </summary>
    /// <param name="key">The tracking key for the store.</param>
    /// <param name="ensure">The creation callback.</param>
    private static async Task EnsureOnceAsync(string key, Func<Task> ensure)
    {
        if (InitializedStores.Contains(key))
        {
            return;
        }

        await SchemaLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!InitializedStores.Add(key))
            {
                return;
            }

            await ensure().ConfigureAwait(false);
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    /// <summary>
    ///     Builds the tracking key for one-time schema creation.
    /// </summary>
    /// <param name="axis">The durable axis that owns the table.</param>
    /// <param name="provider">The database provider backing the store.</param>
    /// <param name="connectionString">The provider connection string.</param>
    /// <returns>The tracking key.</returns>
    private static string StoreKey(string axis, EfCoreStorageProvider provider, string connectionString)
    {
        return $"{axis}:{provider}:{connectionString}";
    }

    /// <summary>
    ///     Passes reset SQL built from test-owned schema and table names to EF Core.
    /// </summary>
    /// <param name="sql">The SQL text; its identifiers come from the test store options, never from input.</param>
    /// <returns>The SQL text unchanged.</returns>
    private static string TestSql(string sql) => sql;
}
