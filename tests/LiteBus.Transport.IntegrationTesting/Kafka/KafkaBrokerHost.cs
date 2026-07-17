using DotNet.Testcontainers.Containers;
using LiteBus.Transport.Kafka;
using Testcontainers.Kafka;
using Testcontainers.Redpanda;

namespace LiteBus.Transport.IntegrationTesting.Kafka;

/// <summary>
///     Manages a Kafka-compatible test container for transport integration tests.
/// </summary>
public sealed class KafkaBrokerHost : IAsyncDisposable
{
    /// <summary>
    ///     Environment variable that overrides the Kafka test container image.
    /// </summary>
    public const string TestImageEnvironmentVariable = "LITEBUS_KAFKA_TEST_IMAGE";

    /// <summary>
    ///     Environment variable that selects the broker implementation (<c>redpanda</c> or <c>confluent</c>).
    /// </summary>
    public const string TestBrokerEnvironmentVariable = "LITEBUS_KAFKA_TEST_BROKER";

    /// <summary>
    ///     The default Redpanda image used for fast Kafka-compatible integration tests.
    /// </summary>
    public const string DefaultRedpandaImage = "docker.redpanda.com/redpandadata/redpanda:v24.3.7";

    /// <summary>
    ///     The default Confluent Platform image used for Kafka integration tests.
    /// </summary>
    public const string DefaultConfluentImage = "confluentinc/cp-kafka:7.5.12";

    /// <summary>
    ///     The maximum time allowed for container startup and broker warmup.
    /// </summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    ///     The running Kafka-compatible test container.
    /// </summary>
    private IContainer? _container;

    /// <summary>
    ///     Gets a value indicating whether the broker started successfully.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>
    ///     Gets the transport options for the started Kafka container.
    /// </summary>
    public KafkaTransportOptions TransportOptions { get; private set; } = null!;

    /// <summary>
    ///     Starts the Kafka test container when Docker is available.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel container startup.</param>
    /// <returns>A task that completes when the broker is ready or startup fails.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsAvailable)
        {
            return;
        }

        try
        {
            await DockerTestGate.RunAsync(async () =>
            {
                var bootstrapServers = await StartSelectedBrokerAsync(cancellationToken).ConfigureAwait(false);

                TransportOptions = new KafkaTransportOptions
                {
                    BootstrapServers = bootstrapServers,
                    ConsumerGroupId = $"litebus-test-{Guid.NewGuid():N}",
                    ClientId = "litebus-transport-integration-tests"
                };

                await KafkaTransportTestInfrastructure.WarmupBrokerAsync(TransportOptions.BootstrapServers)
                    .ConfigureAwait(false);

                IsAvailable = true;
            }).WaitAsync(StartupTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ShouldTreatAsUnavailable(exception))
        {
            await DisposeAsync().ConfigureAwait(false);

            if (DockerTestGate.IsStrictTransportMode)
            {
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        IsAvailable = false;

        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
            _container = null;
        }
    }

    /// <summary>
    ///     Starts the configured broker implementation and returns bootstrap servers in <c>host:port</c> form.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel container startup.</param>
    /// <returns>The bootstrap servers for Confluent clients.</returns>
    private async Task<string> StartSelectedBrokerAsync(CancellationToken cancellationToken)
    {
        return ResolveBrokerKind() switch
        {
            KafkaTestBrokerKind.Confluent => await StartConfluentKafkaAsync(cancellationToken).ConfigureAwait(false),
            _ => await StartRedpandaAsync(cancellationToken).ConfigureAwait(false)
        };
    }

    /// <summary>
    ///     Starts a Redpanda container configured for Kafka API compatibility.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel container startup.</param>
    /// <returns>The bootstrap servers in <c>host:port</c> form.</returns>
    private async Task<string> StartRedpandaAsync(CancellationToken cancellationToken)
    {
        var container = new RedpandaBuilder(ResolveRedpandaImage())
            .Build();

        _container = container;

        await container.StartAsync(cancellationToken)
            .WaitAsync(StartupTimeout, cancellationToken)
            .ConfigureAwait(false);

        return $"{container.Hostname}:{container.GetMappedPublicPort(RedpandaBuilder.RedpandaPort)}";
    }

    /// <summary>
    ///     Starts the Confluent Platform Kafka container with ZooKeeper compatibility settings.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel container startup.</param>
    /// <returns>The bootstrap servers in <c>host:port</c> form.</returns>
    /// <remarks>
    ///     Uses the Debian-based Confluent Platform image pinned by <see cref="DefaultConfluentImage" /> so
    ///     librdkafka native bindings avoid Alpine musl/glibc mismatches during integration tests.
    /// </remarks>
    private async Task<string> StartConfluentKafkaAsync(CancellationToken cancellationToken)
    {
        var container = new KafkaBuilder(ResolveConfluentImage())
            .WithVendor(KafkaVendor.Confluent)
            .Build();

        _container = container;

        await container.StartAsync(cancellationToken)
            .WaitAsync(StartupTimeout, cancellationToken)
            .ConfigureAwait(false);

        return ResolveBootstrapServers(container);
    }

    /// <summary>
    ///     Resolves which broker implementation to start for the current test run.
    /// </summary>
    /// <returns>The selected broker kind.</returns>
    private static KafkaTestBrokerKind ResolveBrokerKind()
    {
        var configuredBroker = Environment.GetEnvironmentVariable(TestBrokerEnvironmentVariable);

        if (string.Equals(configuredBroker, "confluent", StringComparison.OrdinalIgnoreCase))
        {
            return KafkaTestBrokerKind.Confluent;
        }

        if (string.Equals(configuredBroker, "redpanda", StringComparison.OrdinalIgnoreCase))
        {
            return KafkaTestBrokerKind.Redpanda;
        }

        var configuredImage = Environment.GetEnvironmentVariable(TestImageEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configuredImage) &&
            configuredImage.Contains("cp-kafka", StringComparison.OrdinalIgnoreCase))
        {
            return KafkaTestBrokerKind.Confluent;
        }

        return KafkaTestBrokerKind.Redpanda;
    }

    /// <summary>
    ///     Resolves the Redpanda image from the environment or the default fast-start image.
    /// </summary>
    /// <returns>The Redpanda container image reference.</returns>
    private static string ResolveRedpandaImage()
    {
        var configuredImage = Environment.GetEnvironmentVariable(TestImageEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configuredImage) &&
            !configuredImage.Contains("cp-kafka", StringComparison.OrdinalIgnoreCase) &&
            !configuredImage.Contains("confluent-local", StringComparison.OrdinalIgnoreCase))
        {
            return configuredImage;
        }

        return DefaultRedpandaImage;
    }

    /// <summary>
    ///     Resolves the Confluent Kafka image from the environment or the Testcontainers default.
    /// </summary>
    /// <returns>The Confluent Kafka container image reference.</returns>
    private static string ResolveConfluentImage()
    {
        var configuredImage = Environment.GetEnvironmentVariable(TestImageEnvironmentVariable);

        return string.IsNullOrWhiteSpace(configuredImage)
            ? DefaultConfluentImage
            : configuredImage;
    }

    /// <summary>
    ///     Resolves a librdkafka-compatible bootstrap server list from the running container.
    /// </summary>
    /// <param name="container">The started Kafka container.</param>
    /// <returns>The bootstrap servers in <c>host:port</c> form.</returns>
    /// <remarks>
    ///     <see cref="KafkaContainer.GetBootstrapAddress" /> returns a <c>plaintext://</c> URI that Confluent clients
    ///     mishandle, causing metadata redirects to the container's internal <c>9092</c> listener.
    /// </remarks>
    private static string ResolveBootstrapServers(KafkaContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        return $"{container.Hostname}:{container.GetMappedPublicPort(KafkaBuilder.KafkaPort)}";
    }

    /// <summary>
    ///     Determines whether startup failure should mark the broker unavailable instead of failing the test host.
    /// </summary>
    /// <param name="exception">The exception raised while starting the broker.</param>
    /// <returns><see langword="true" /> when strict transport mode is disabled.</returns>
    private static bool ShouldTreatAsUnavailable(Exception exception)
    {
        if (DockerTestGate.IsStrictTransportMode)
        {
            return false;
        }

        return exception is TimeoutException or InvalidOperationException;
    }

    /// <summary>
    ///     Supported Kafka-compatible broker implementations for integration tests.
    /// </summary>
    private enum KafkaTestBrokerKind
    {
        /// <summary>
        ///     Redpanda broker using the Kafka API.
        /// </summary>
        Redpanda,

        /// <summary>
        ///     Confluent Platform Kafka broker.
        /// </summary>
        Confluent
    }
}
