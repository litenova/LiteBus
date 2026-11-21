using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

// <docs-snippet efcore-outbox-immediate-handler>
namespace LiteBus.Samples.Documentation.Orders;

/// <summary>
///     Updates the order projection as soon as the order event is published.
/// </summary>
[DispatchImmediately]
public sealed class OrderProjectionHandler : IEventHandler<OrderSubmitted>
{
    /// <summary>
    ///     The projection store updated by the handler.
    /// </summary>
    private readonly IOrderProjectionStore _projectionStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OrderProjectionHandler" /> class.
    /// </summary>
    /// <param name="projectionStore">The projection store updated by the handler.</param>
    public OrderProjectionHandler(IOrderProjectionStore projectionStore)
    {
        _projectionStore = projectionStore;
    }

    /// <inheritdoc />
    public Task HandleAsync(OrderSubmitted message, CancellationToken cancellationToken = default)
    {
        return _projectionStore.UpsertAsync(message.OrderId, cancellationToken);
    }
}
// </docs-snippet>
