using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LiteBus.PostgreSql.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    /// <summary>
    ///     Message shown when integration tests are skipped because Docker is not available.
    /// </summary>
    public const string DockerRequiredMessage =
        "PostgreSQL integration tests require Docker. Start Docker Desktop (or the Docker daemon) and run the tests again.";

    private PostgreSqlContainer? _container;

    public bool IsDockerAvailable { get; private set; }

    public NpgsqlDataSource? DataSource { get; private set; }

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();

        try
        {
            await _container.StartAsync();
            IsDockerAvailable = true;
            DataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        }
        catch (Exception exception) when (IsDockerUnavailable(exception))
        {
            IsDockerAvailable = false;
            await _container.DisposeAsync();
            _container = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null)
        {
            await DataSource.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static bool IsDockerUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().FullName == "DotNet.Testcontainers.Builders.DockerUnavailableException")
            {
                return true;
            }
        }

        return false;
    }
}
