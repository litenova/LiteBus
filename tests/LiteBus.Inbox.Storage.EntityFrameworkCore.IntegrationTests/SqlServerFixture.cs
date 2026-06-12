using Testcontainers.MsSql;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Shared SQL Server container for Entity Framework Core inbox integration tests.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    /// <summary>
    ///     Message shown when integration tests fail because Docker is not available.
    /// </summary>
    public const string DockerRequiredMessage =
        "SQL Server integration tests require Docker. Start Docker Desktop (or the Docker daemon), clear a stale DOCKER_HOST override (tcp://...), and run the tests again.";

    /// <summary>
    ///     Optional connection string that bypasses Testcontainers when set (local troubleshooting only).
    /// </summary>
    public const string ConnectionStringEnvironmentVariable = "LITEBUS_TEST_SQLSERVER_CONNECTION_STRING";

    private const int MaxAttempts = 30;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private MsSqlContainer? _container;

    /// <summary>
    ///     Gets the SQL Server connection string used by test database contexts.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            ConnectionString = connectionString;
            return;
        }

        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                _container = new MsSqlBuilder()
                    .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                    .Build();

                await _container.StartAsync().ConfigureAwait(false);
                ConnectionString = _container.GetConnectionString();
                return;
            }
            catch (Exception exception)
            {
                lastException = exception;

                if (_container is not null)
                {
                    await _container.DisposeAsync().ConfigureAwait(false);
                    _container = null;
                }

                if (attempt < MaxAttempts)
                {
                    await Task.Delay(RetryDelay).ConfigureAwait(false);
                }
            }
        }

        throw new InvalidOperationException(DockerRequiredMessage, lastException);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}