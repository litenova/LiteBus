using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Leases due outbox messages for one publisher instance.
/// </summary>
/// <remarks>
///     <para>
///         A lease gives a processor temporary ownership of pending work. Store implementations should lease messages
///         atomically, set the owner and expiration timestamp, and increment the attempt count before returning the
///         envelopes. Competing processors must not receive the same message while the lease is active.
///     </para>
///     <para>
///         Database-backed stores should use the local database mechanism for skip-locked reads or an equivalent claim
///         operation. Expired leases should become eligible for another processor so a crashed publisher does not leave
///         messages stuck in the publishing state.
///     </para>
/// </remarks>
public interface IOutboxMessageLeaseStore
{
    /// <summary>
    ///     Leases pending, failed, or expired publishing messages for one processing pass.
    /// </summary>
    /// <param name="request">
    ///     The batch size, owner name, lease duration, and current time used to decide which messages can be claimed.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the lease operation before ownership is recorded.</param>
    /// <returns>The envelopes claimed by the caller, ordered according to the store policy.</returns>
    Task<IReadOnlyList<OutboxMessageEnvelope>> LeasePendingAsync(
        OutboxLeaseRequest request,
        CancellationToken cancellationToken = default);
}