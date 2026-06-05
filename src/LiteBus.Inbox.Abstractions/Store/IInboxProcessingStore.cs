namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Composite inbox store role used by processors to accept, lease, and persist envelope state transitions.
/// </summary>
/// <remarks>
///     Combines <see cref="IInboxStore" />, <see cref="IInboxLeaseStore" />, and <see cref="IInboxStateWriter" />.
///     Fine-grained interfaces remain available for callers that depend on a single concern.
/// </remarks>
public interface IInboxProcessingStore : IInboxStore, IInboxLeaseStore, IInboxStateWriter
{
}
