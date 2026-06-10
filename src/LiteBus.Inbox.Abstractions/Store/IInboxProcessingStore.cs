namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Composite inbox store role used by processors to lease and persist envelope state transitions.
/// </summary>
/// <remarks>
///     Combines <see cref="IInboxLeaseStore" /> and <see cref="IInboxStateWriter" /> only. Acceptance code uses
///     <see cref="IInboxStore" /> directly.
/// </remarks>
public interface IInboxProcessingStore : IInboxLeaseStore, IInboxStateWriter
{
}
