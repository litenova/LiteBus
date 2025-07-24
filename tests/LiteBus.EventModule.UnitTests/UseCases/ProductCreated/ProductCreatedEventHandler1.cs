using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.ProductCreated;

public sealed class ProductCreatedEventHandler1 : IFilteredEventHandler, IEventHandler<ProductCreatedEvent>
{
    public Task HandleAsync(ProductCreatedEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}