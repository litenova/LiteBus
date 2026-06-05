namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Composite outbox store role used by processors to accept, lease, and persist envelope state transitions.
/// </summary>
/// <remarks>
///     Combines <see cref="IOutboxStore" />, <see cref="IOutboxLeaseStore" />, and <see cref="IOutboxStateWriter" />.
///     Fine-grained interfaces remain available for callers that depend on a single concern.
/// </remarks>
public interface IOutboxProcessingStore : IOutboxStore, IOutboxLeaseStore, IOutboxStateWriter
{
}
