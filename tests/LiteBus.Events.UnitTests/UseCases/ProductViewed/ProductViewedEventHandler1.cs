using LiteBus.Events.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.ProductViewed;

public sealed class ProductViewedEventHandler1<TViewSource> : IEventHandler<ProductViewedEvent<TViewSource>>
{
    public Task HandleAsync(ProductViewedEvent<TViewSource> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}