using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.ProductViewed;

public sealed class ProductViewedEventHandlerPreHandler<TViewSource> : IEventPreHandler<ProductViewedEvent<TViewSource>>
{
    public Task PreHandleAsync(ProductViewedEvent<TViewSource> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}