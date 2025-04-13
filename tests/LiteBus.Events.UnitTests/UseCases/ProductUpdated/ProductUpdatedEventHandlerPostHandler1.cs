using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.ProductUpdated;

[HandlerOrder(1)]
public sealed class ProductUpdatedEventHandlerPostHandler1 : IEventPostHandler<ProductUpdatedEvent>
{
    public Task PostHandleAsync(ProductUpdatedEvent message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}