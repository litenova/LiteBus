using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.ProductCreated;

[HandlerOrder(2)]
public sealed class ProductCreatedEventHandlerPostHandler2 : IEventPostHandler<ProductCreatedEvent>
{
    public Task PostHandleAsync(ProductCreatedEvent message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}