using System.Threading.Channels;

namespace LiteBus.Transport.InMemory;

/// <summary>
///     One destination endpoint backed by a bounded channel.
/// </summary>
internal sealed class InMemoryDestinationEndpoint : IDisposable
{
    /// <summary>
    ///     Gets the channel carrying pending deliveries for the destination.
    /// </summary>
    private readonly Channel<InMemoryPendingDelivery> _channel;

    /// <summary>
    ///     Limits the total queued and in-flight deliveries admitted for the destination.
    /// </summary>
    private readonly SemaphoreSlim _capacity;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryDestinationEndpoint" /> class.
    /// </summary>
    /// <param name="capacity">The maximum number of unsettled deliveries admitted for the destination.</param>
    internal InMemoryDestinationEndpoint(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _channel = Channel.CreateBounded<InMemoryPendingDelivery>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        _capacity = new SemaphoreSlim(capacity, capacity);
    }

    /// <summary>
    ///     Gets the reader used by consumers.
    /// </summary>
    internal ChannelReader<InMemoryPendingDelivery> Reader => _channel.Reader;

    /// <summary>
    ///     Enqueues a newly published delivery after reserving destination capacity.
    /// </summary>
    /// <param name="delivery">The delivery to enqueue.</param>
    /// <param name="cancellationToken">The token used to cancel capacity admission or the channel write.</param>
    /// <returns>A task that completes when the delivery has been admitted.</returns>
    internal async Task EnqueueAsync(
        InMemoryPendingDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        await _capacity.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _channel.Writer.WriteAsync(delivery, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _capacity.Release();
            throw;
        }
    }

    /// <summary>
    ///     Returns a delivery to the channel without acquiring another capacity reservation.
    /// </summary>
    /// <param name="delivery">The redelivered message to enqueue.</param>
    /// <param name="cancellationToken">The token used to cancel the channel write.</param>
    /// <returns>A task that completes when the delivery has been returned.</returns>
    internal async Task RequeueAsync(
        InMemoryPendingDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        await _channel.Writer.WriteAsync(delivery, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Releases the capacity reservation held by a settled delivery.
    /// </summary>
    internal void ReleaseCapacity()
    {
        _capacity.Release();
    }

    /// <summary>
    ///     Completes the destination channel so pending and future writes fail.
    /// </summary>
    /// <returns><see langword="true" /> when this call completed the channel.</returns>
    internal bool TryComplete()
    {
        return _channel.Writer.TryComplete();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _capacity.Dispose();
    }
}
