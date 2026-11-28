using Testcontainers.Azurite;

namespace LiteBus.Storage.IntegrationTests.Auditing.Anchors.AzureBlob;

/// <summary>
///     Shared Azurite container for Azure Blob anchor integration tests. Initialization marks the fixture unavailable when Docker is not running.
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    /// <summary>
    ///     Message shown when integration tests fail because Docker is not available.
    /// </summary>
    public const string DockerRequiredMessage =
        "Azure Blob anchor integration tests require Docker. Start Docker Desktop (or the Docker daemon), clear a stale DOCKER_HOST override (tcp://...), and run the tests again.";

    /// <summary>
    ///     Optional connection string that bypasses Testcontainers when set (local troubleshooting only).
    /// </summary>
    public const string ConnectionStringEnvironmentVariable = "LITEBUS_TEST_AZURITE_CONNECTION_STRING";

    private const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.28.0";

    private AzuriteContainer? _container;

    /// <summary>
    ///     Gets the connection string used to reach Azurite blob storage.
    /// </summary>
    public string ConnectionString { get; private set; } = null!;

    /// <summary>
    ///     Gets a value indicating whether the fixture connected to Azurite successfully.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");

        if (dockerHost?.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) == true)
        {
            Environment.SetEnvironmentVariable("DOCKER_HOST", null);
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            ConnectionString = connectionString;
            IsAvailable = true;
            return;
        }

        try
        {
            _container = new AzuriteBuilder(AzuriteImage)
                .WithCommand("--skipApiVersionCheck")
                .Build();

            await _container.StartAsync().ConfigureAwait(false);

            ConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception exception) when (DockerAvailability.IsDockerUnavailable(exception))
        {
            if (_container is not null)
            {
                await _container.DisposeAsync().ConfigureAwait(false);
                _container = null;
            }

            IsAvailable = false;
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
