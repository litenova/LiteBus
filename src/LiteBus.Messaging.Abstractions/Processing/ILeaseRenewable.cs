using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Extends an active processing lease while dispatch work is still running.
/// </summary>
public interface ILeaseRenewable
{
    /// <summary>
    ///     Extends the lease expiration for one in-flight message owned by the supplied worker.
    /// </summary>
    /// <param name="messageId">The identifier of the leased message.</param>
    /// <param name="leaseOwner">The worker name that currently owns the lease.</param>
    /// <param name="expiresAt">The new UTC expiration timestamp written to storage.</param>
    /// <param name="cancellationToken">A token that cancels the renewal before it is committed.</param>
    /// <returns>
    ///     <see langword="true" /> when the row was still processing under the supplied owner; otherwise
    ///     <see langword="false" />.
    /// </returns>
    Task<bool> RenewLeaseAsync(
        Guid messageId,
        string leaseOwner,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}