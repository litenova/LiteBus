using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Events.UseCases.ProductUpdated;

[HandlerPriority(2)]
public sealed class ProductUpdatedEventHandlerPostHandler2 : IEventPostHandler<ProductUpdatedEvent>
{
    public Task PostHandleAsync(ProductUpdatedEvent message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}