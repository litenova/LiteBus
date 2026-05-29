using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Leases due inbox commands for one worker instance.
/// </summary>
/// <remarks>
///     <para>
///         A lease gives a processor temporary ownership of commands that are ready to run. Implementations should claim
///         messages atomically, set the owner and expiration timestamp, and increment the attempt count before returning
///         the envelopes. Other processors must not receive the same command while the lease is active.
///     </para>
///     <para>
///         Expired leases should become eligible for another processor. This gives the inbox at-least-once execution
///         behavior after a worker crash or process shutdown.
///     </para>
/// </remarks>
public interface ICommandInboxLeaseStore
{
    /// <summary>
    ///     Leases pending, failed, or expired processing commands for one processing pass.
    /// </summary>
    /// <param name="request">
    ///     The batch size, owner name, lease duration, and current time used to decide which commands can be claimed.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the lease operation before ownership is recorded.</param>
    /// <returns>The command envelopes claimed by the caller, ordered according to the store policy.</returns>
    Task<IReadOnlyList<InboxCommandEnvelope>> LeasePendingAsync(
        InboxLeaseRequest request,
        CancellationToken cancellationToken = default);
}