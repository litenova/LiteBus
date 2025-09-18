using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.ProductCreated;

public sealed class ProductCreatedEventHandlerPreHandler : IEventPreHandler<ProductCreatedEvent>
{
    public Task PreHandleAsync(ProductCreatedEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}