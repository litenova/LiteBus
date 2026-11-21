using Testcontainers.Minio;

namespace LiteBus.Storage.IntegrationTests.Auditing.Anchors.S3;

/// <summary>
///     Shared MinIO container for S3 anchor integration tests. Initialization marks the fixture unavailable when Docker is not running.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    /// <summary>
    ///     Message shown when integration tests fail because Docker is not available.
    /// </summary>
    public const string DockerRequiredMessage =
        "S3 anchor integration tests require Docker. Start Docker Desktop (or the Docker daemon), clear a stale DOCKER_HOST override (tcp://...), and run the tests again.";

    /// <summary>
    ///     Optional endpoint that bypasses Testcontainers when set (local troubleshooting only).
    /// </summary>
    public const string EndpointEnvironmentVariable = "LITEBUS_TEST_MINIO_ENDPOINT";

    /// <summary>
    ///     Optional access key used with the endpoint override (local troubleshooting only).
    /// </summary>
    public const string AccessKeyEnvironmentVariable = "LITEBUS_TEST_MINIO_ACCESS_KEY";

    /// <summary>
    ///     Optional secret key used with the endpoint override (local troubleshooting only).
    /// </summary>
    public const string SecretKeyEnvironmentVariable = "LITEBUS_TEST_MINIO_SECRET_KEY";

    private const string MinioImage = "minio/minio:RELEASE.2023-01-31T02-24-19Z";
    private const string MinioAccessKey = "litebus-test-user";
    private const string MinioSecretKey = "litebus-test-password";

    private MinioContainer? _container;

    /// <summary>
    ///     Gets the service URL used to reach the S3-compatible endpoint.
    /// </summary>
    public string ServiceUrl { get; private set; } = null!;

    /// <summary>
    ///     Gets the access key used to authenticate with the endpoint.
    /// </summary>
    public string AccessKey { get; private set; } = null!;

    /// <summary>
    ///     Gets the secret key used to authenticate with the endpoint.
    /// </summary>
    public string SecretKey { get; private set; } = null!;

    /// <summary>
    ///     Gets a value indicating whether the fixture connected to MinIO successfully.
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

        var endpointOverride = Environment.GetEnvironmentVariable(EndpointEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(endpointOverride))
        {
            ServiceUrl = endpointOverride;
            AccessKey = Environment.GetEnvironmentVariable(AccessKeyEnvironmentVariable) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(AccessKey))
            {
                AccessKey = MinioAccessKey;
            }

            SecretKey = Environment.GetEnvironmentVariable(SecretKeyEnvironmentVariable) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(SecretKey))
            {
                SecretKey = MinioSecretKey;
            }

            IsAvailable = true;
            return;
        }

        try
        {
            _container = new MinioBuilder(MinioImage)
                .WithUsername(MinioAccessKey)
                .WithPassword(MinioSecretKey)
                .Build();

            await _container.StartAsync().ConfigureAwait(false);

            ServiceUrl = _container.GetConnectionString();
            AccessKey = _container.GetAccessKey();
            SecretKey = _container.GetSecretKey();
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
