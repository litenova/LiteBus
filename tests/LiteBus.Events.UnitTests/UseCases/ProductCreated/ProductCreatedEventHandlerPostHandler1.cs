using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.ProductCreated;

[HandlerOrder(1)]
public sealed class ProductCreatedEventHandlerPostHandler1 : IEventPostHandler<ProductCreatedEvent>
{
    public Task PostHandleAsync(ProductCreatedEvent message, object messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}