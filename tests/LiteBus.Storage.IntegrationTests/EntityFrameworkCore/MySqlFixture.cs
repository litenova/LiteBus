using Testcontainers.MySql;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Provides one MySQL container for Entity Framework Core storage contract tests.
/// </summary>
public sealed class MySqlFixture : IAsyncLifetime
{
    /// <summary>
    ///     The environment variable that can replace the Testcontainers connection string.
    /// </summary>
    public const string ConnectionStringEnvironmentVariable = "LITEBUS_TEST_MYSQL_CONNECTION_STRING";

    /// <summary>
    ///     The error shown when the MySQL test container cannot start.
    /// </summary>
    public const string DockerRequiredMessage =
        "MySQL integration tests require Docker. Start Docker Desktop or set LITEBUS_TEST_MYSQL_CONNECTION_STRING.";

    /// <summary>
    ///     The container started by this fixture, when an external connection string is not supplied.
    /// </summary>
    private MySqlContainer? _container;

    /// <summary>
    ///     Gets the connection string used to create provider-specific test databases.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var externalConnectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            ConnectionString = externalConnectionString;
            return;
        }

        try
        {
            _container = new MySqlBuilder("mysql:8.4")
                .WithDatabase("litebus_tests")
                .Build();

            await _container.StartAsync().ConfigureAwait(false);
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception exception)
        {
            if (_container is not null)
            {
                await _container.DisposeAsync().ConfigureAwait(false);
                _container = null;
            }

            throw new InvalidOperationException(DockerRequiredMessage, exception);
        }
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
