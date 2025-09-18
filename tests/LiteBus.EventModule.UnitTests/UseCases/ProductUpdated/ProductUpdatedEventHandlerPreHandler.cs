using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.ProductUpdated;

public sealed class ProductUpdatedEventHandlerPreHandler : IEventPreHandler<ProductUpdatedEvent>
{
    public Task PreHandleAsync(ProductUpdatedEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}