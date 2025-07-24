using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.ProductViewed;

public sealed class ProductViewedEvent<TViewSource> : IAuditableEvent, IEvent
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public required TViewSource ViewSource { get; init; }

    public List<Type> ExecutedTypes { get; } = new();
}