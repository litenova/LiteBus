using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LiteBus.PostgreSql.IntegrationTests;

/// <summary>
///     Shared PostgreSQL container for integration tests. Initialization fails when Docker is unavailable.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    /// <summary>
    ///     Message shown when integration tests fail because Docker is not available.
    /// </summary>
    public const string DockerRequiredMessage =
        "PostgreSQL integration tests require Docker. Start Docker Desktop (or the Docker daemon), clear a stale DOCKER_HOST override (tcp://...), and run the tests again.";

    /// <summary>
    ///     Optional connection string that bypasses Testcontainers when set (local troubleshooting only).
    /// </summary>
    public const string ConnectionStringEnvironmentVariable = "LITEBUS_TEST_POSTGRES_CONNECTION_STRING";

    private const int MaxAttempts = 30;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private PostgreSqlContainer? _container;

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        ConfigureDockerHostForCurrentPlatform();

        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            DataSource = NpgsqlDataSource.Create(connectionString);
            return;
        }

        await WaitForDockerDaemonAsync();

        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                _container = new PostgreSqlBuilder()
                    .WithImage("postgres:16-alpine")
                    .Build();

                await _container.StartAsync();
                DataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
                return;
            }
            catch (Exception exception) when (IsDockerUnavailable(exception))
            {
                lastException = exception;

                if (_container is not null)
                {
                    await _container.DisposeAsync();
                    _container = null;
                }

                if (attempt < MaxAttempts)
                {
                    await Task.Delay(RetryDelay);
                }
            }
        }

        throw new InvalidOperationException(DockerRequiredMessage, lastException);
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

    private static void ConfigureDockerHostForCurrentPlatform()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");

        if (dockerHost?.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) == true)
        {
            Environment.SetEnvironmentVariable("DOCKER_HOST", null);
        }
    }

    private static async Task WaitForDockerDaemonAsync()
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                if (await CanConnectToDockerAsync())
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                lastException = exception;
            }

            if (attempt < MaxAttempts)
            {
                await Task.Delay(RetryDelay);
            }
        }

        throw new InvalidOperationException(DockerRequiredMessage, lastException);
    }

    private static async Task<bool> CanConnectToDockerAsync()
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "ps --format {{.ID}}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
        {
            return false;
        }

        var waitTask = process.WaitForExitAsync();
        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(15))) == waitTask;

        if (!completed)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort when the CLI hangs against an unavailable daemon.
            }

            return false;
        }

        return process.ExitCode == 0;
    }

    private static bool IsDockerUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var typeName = current.GetType().FullName;
            if (typeName is "DotNet.Testcontainers.Builders.DockerUnavailableException"
                or "DotNet.Testcontainers.Guard.ArgumentException"
                or "Docker.DotNet.DockerApiException")
            {
                return true;
            }
        }

        return false;
    }
}
