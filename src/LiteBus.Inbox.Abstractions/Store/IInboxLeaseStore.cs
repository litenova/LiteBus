using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Leases due inbox envelopes for one worker instance.
/// </summary>
/// <remarks>
///     <para>
///         A lease gives a processor temporary ownership of envelopes that are ready to run. Implementations should claim
///         rows atomically, set the owner and expiration timestamp, and increment the attempt count before returning the
///         envelopes. Other processors must not receive the same envelope while the lease is active.
///     </para>
///     <para>
///         Expired leases should become eligible for another processor. This gives the inbox at-least-once execution
///         behavior after a worker crash or process shutdown.
///     </para>
/// </remarks>
public interface IInboxLeaseStore
{
    /// <summary>
    ///     Leases pending, failed, or expired processing envelopes for one processing pass.
    /// </summary>
    /// <param name="request">
    ///     The batch size, owner name, lease duration, and current time used to decide which envelopes can be claimed.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the lease operation before ownership is recorded.</param>
    /// <returns>The envelopes claimed by the caller, ordered according to the store policy.</returns>
    Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
        InboxLeaseRequest request,
        CancellationToken cancellationToken = default);
}