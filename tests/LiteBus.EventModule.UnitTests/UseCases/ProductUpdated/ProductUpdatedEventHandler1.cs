using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.ProductUpdated;

public sealed class ProductUpdatedEventHandler1 : IEventHandler<ProductUpdatedEvent>
{
    public Task HandleAsync(ProductUpdatedEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}