using LiteBus.Transport.Kafka;

namespace LiteBus.Transport.IntegrationTesting.Kafka;

/// <summary>
///     xUnit collection fixture that shares one Kafka container across durable transport integration tests.
/// </summary>
public sealed class KafkaBrokerFixture : IAsyncLifetime
{
    /// <summary>
    ///     The maximum time allowed for shared broker fixture initialization.
    /// </summary>
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    ///     The shared broker host acquired for this collection fixture instance.
    /// </summary>
    private KafkaBrokerHost? _host;

    /// <summary>
    ///     Gets a value indicating whether the shared Kafka broker started successfully.
    /// </summary>
    public bool IsAvailable => _host?.IsAvailable ?? false;

    /// <summary>
    ///     Gets the transport options for the started Kafka container.
    /// </summary>
    public KafkaTransportOptions TransportOptions => _host!.TransportOptions;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        using var cancellationTokenSource = new CancellationTokenSource(InitializationTimeout);

        try
        {
            _host = await KafkaBrokerSharedLifecycle.AcquireAsync(cancellationTokenSource.Token)
                .WaitAsync(InitializationTimeout, cancellationTokenSource.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!DockerTestGate.IsStrictTransportMode && IsInitializationFailure(exception))
        {
            _host = null;
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await KafkaBrokerSharedLifecycle.ReleaseAsync(_host).ConfigureAwait(false);
            _host = null;
        }
    }

    /// <summary>
    ///     Determines whether fixture initialization failed in a way that should skip tests locally.
    /// </summary>
    /// <param name="exception">The exception raised while initializing the fixture.</param>
    /// <returns><see langword="true" /> when tests should be skipped instead of failing the host process.</returns>
    private static bool IsInitializationFailure(Exception exception)
    {
        return exception is TimeoutException or InvalidOperationException;
    }
}
