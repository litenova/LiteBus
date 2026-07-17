namespace LiteBus.Transport.InMemory;

/// <summary>
///     Settles one in-memory delivery while preserving its destination capacity reservation across requeue.
/// </summary>
internal sealed class InMemoryDeliverySettlement
{
    /// <summary>
    ///     Indicates that the delivery has not been settled.
    /// </summary>
    private const int Pending = 0;

    /// <summary>
    ///     Indicates that the delivery has been settled or is being returned to the channel.
    /// </summary>
    private const int Settled = 1;

    /// <summary>
    ///     Gets the endpoint that owns the delivery capacity reservation.
    /// </summary>
    private readonly InMemoryDestinationEndpoint _endpoint;

    /// <summary>
    ///     Gets the delivery being settled.
    /// </summary>
    private readonly InMemoryPendingDelivery _delivery;

    /// <summary>
    ///     Tracks whether settlement has already started.
    /// </summary>
    private int _state;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryDeliverySettlement" /> class.
    /// </summary>
    /// <param name="endpoint">The endpoint that owns the delivery capacity reservation.</param>
    /// <param name="delivery">The delivery being settled.</param>
    internal InMemoryDeliverySettlement(
        InMemoryDestinationEndpoint endpoint,
        InMemoryPendingDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(delivery);
        _endpoint = endpoint;
        _delivery = delivery;
    }

    /// <summary>
    ///     Accepts or discards the delivery and releases its destination capacity reservation.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel settlement before it starts.</param>
    /// <returns>A completed task after settlement.</returns>
    internal Task ReleaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref _state, Settled, Pending) == Pending)
        {
            _endpoint.ReleaseCapacity();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Negative-acknowledges the delivery by either requeueing it or releasing its capacity reservation.
    /// </summary>
    /// <param name="requeue">A value indicating whether the delivery should be returned for redelivery.</param>
    /// <param name="cancellationToken">The token used to cancel settlement.</param>
    /// <returns>A task that completes when settlement finishes.</returns>
    internal Task RejectAsync(bool requeue, CancellationToken cancellationToken)
    {
        return requeue
            ? RequeueAsync(cancellationToken)
            : ReleaseAsync(cancellationToken);
    }

    /// <summary>
    ///     Returns the delivery to the endpoint without acquiring a second capacity reservation.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the channel write.</param>
    /// <returns>A task that completes when the delivery has been returned.</returns>
    internal async Task RequeueAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref _state, Settled, Pending) != Pending)
        {
            return;
        }

        var completed = false;

        try
        {
            await _endpoint.RequeueAsync(CreateRedelivery(), cancellationToken).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                Volatile.Write(ref _state, Pending);
            }
        }
    }

    /// <summary>
    ///     Creates the redelivery snapshot written back to the endpoint.
    /// </summary>
    /// <returns>A copy of the current delivery marked as redelivered.</returns>
    private InMemoryPendingDelivery CreateRedelivery()
    {
        return new InMemoryPendingDelivery
        {
            Body = _delivery.Body,
            Headers = _delivery.Headers,
            Destination = _delivery.Destination,
            Route = _delivery.Route,
            MessageId = _delivery.MessageId,
            CorrelationId = _delivery.CorrelationId,
            Redelivered = true
        };
    }
}
