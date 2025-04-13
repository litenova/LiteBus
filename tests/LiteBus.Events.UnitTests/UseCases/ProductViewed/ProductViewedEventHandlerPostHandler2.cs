using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.ProductViewed;

[HandlerOrder(2)]
public sealed class ProductViewedEventHandlerPostHandler2<TViewSource> : IEventPostHandler<ProductViewedEvent<TViewSource>>
{
    public Task PostHandleAsync(ProductViewedEvent<TViewSource> message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}