using LiteBus.Events.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.ProductCreated;

public sealed class ProductCreatedEventHandler3 : IEventHandler<ProductCreatedEvent>
{
    public Task HandleAsync(ProductCreatedEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}