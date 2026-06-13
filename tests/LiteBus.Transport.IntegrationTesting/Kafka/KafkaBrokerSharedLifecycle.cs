namespace LiteBus.Transport.IntegrationTesting.Kafka;

/// <summary>
///     Shares one Kafka test container across xUnit collection fixtures in parallel test assemblies.
/// </summary>
internal static class KafkaBrokerSharedLifecycle
{
    /// <summary>
    ///     Serializes acquire and release operations for the shared broker host.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    ///     The process-wide broker host started for Kafka integration tests.
    /// </summary>
    private static KafkaBrokerHost? _sharedHost;

    /// <summary>
    ///     The number of active collection fixtures referencing the shared broker host.
    /// </summary>
    private static int _referenceCount;

    /// <summary>
    ///     Acquires the shared broker host, starting the container when it is not already available.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel broker startup.</param>
    /// <returns>The shared broker host for the current test process.</returns>
    public static async Task<KafkaBrokerHost> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _sharedHost ??= new KafkaBrokerHost();

            if (!_sharedHost.IsAvailable)
            {
                await _sharedHost.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_sharedHost.IsAvailable)
            {
                _referenceCount++;
            }

            return _sharedHost;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     Releases one reference to the shared broker host and disposes the container when the last reference is released.
    /// </summary>
    /// <param name="host">The broker host acquired from <see cref="AcquireAsync" />.</param>
    /// <returns>A task that completes when the shared host is released.</returns>
    public static async Task ReleaseAsync(KafkaBrokerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        await Gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (!ReferenceEquals(host, _sharedHost))
            {
                return;
            }

            if (_referenceCount > 0)
            {
                _referenceCount--;
            }

            if (_referenceCount > 0)
            {
                return;
            }

            await _sharedHost.DisposeAsync().ConfigureAwait(false);
            _sharedHost = null;
        }
        finally
        {
            Gate.Release();
        }
    }
}
