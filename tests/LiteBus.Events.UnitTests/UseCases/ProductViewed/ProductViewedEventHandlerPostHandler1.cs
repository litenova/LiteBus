using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.ProductViewed;

[HandlerOrder(1)]
public sealed class ProductViewedEventHandlerPostHandler1<TViewSource> : IEventPostHandler<ProductViewedEvent<TViewSource>>
{
    public Task PostHandleAsync(ProductViewedEvent<TViewSource> message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}