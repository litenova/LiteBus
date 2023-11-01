using LiteBus.Events.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.ProductViewed;

public sealed class ProductViewedEvent<TViewSource> : IAuditableEvent, IEvent
{
    public List<Type> ExecutedTypes { get; } = new();

    public Guid CorrelationId { get; } = Guid.NewGuid();

    public required TViewSource ViewSource { get; init; }
}