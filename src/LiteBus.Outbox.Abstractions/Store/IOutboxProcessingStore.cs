namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Composite outbox store role used by processors to lease and persist envelope state transitions.
/// </summary>
/// <remarks>
///     Combines <see cref="IOutboxLeaseStore" /> and <see cref="IOutboxStateWriter" /> only. Acceptance code uses
///     <see cref="IOutboxStore" /> directly.
/// </remarks>
public interface IOutboxProcessingStore : IOutboxLeaseStore, IOutboxStateWriter
{
}