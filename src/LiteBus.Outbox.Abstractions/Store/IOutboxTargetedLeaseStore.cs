using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Leases specific outbox messages by identifier, for immediate dispatch of the envelopes a mediation just
///     committed.
/// </summary>
/// <remarks>
///     <para>
///         A separate interface from <see cref="IOutboxLeaseStore" />, so stores written outside LiteBus keep compiling.
///         The shipped PostgreSQL, Entity Framework Core and in-memory stores implement it. Immediate dispatch requires
///         it and fails at composition when the registered store does not.
///     </para>
///     <para>
///         The lease is atomic and has the same side effects as <see cref="IOutboxLeaseStore.LeasePendingAsync" />:
///         it sets the owner and expiry, increments the attempt count and advances the lease generation. Only listed
///         messages that are pending and not under an active lease are claimed, regardless of
///         <see cref="OutboxEnvelope.VisibleAfter" />. Messages that cannot be claimed (already leased, published or
///         dead-lettered, or unknown) are skipped and not returned, so two callers never both receive one message.
///     </para>
/// </remarks>
public interface IOutboxTargetedLeaseStore
{
    /// <summary>
    ///     Leases the listed messages that are still claimable.
    /// </summary>
    /// <param name="request">The identifiers to lease, the owner, the lease duration and the current time.</param>
    /// <param name="cancellationToken">A token that cancels the lease operation before ownership is recorded.</param>
    /// <returns>The envelopes claimed by the caller, in no guaranteed order.</returns>
    Task<IReadOnlyList<OutboxEnvelope>> LeaseByIdAsync(
        OutboxTargetedLeaseRequest request,
        CancellationToken cancellationToken = default);
}
