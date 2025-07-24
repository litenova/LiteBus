using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.ProductUpdated;

[HandlerOrder(2)]
public sealed class ProductUpdatedEventHandlerPostHandler2 : IEventPostHandler<ProductUpdatedEvent>
{
    public Task PostHandleAsync(ProductUpdatedEvent message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}