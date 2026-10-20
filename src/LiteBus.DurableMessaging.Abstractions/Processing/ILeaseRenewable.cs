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
    /// <param name="request">The lease renewal inputs.</param>
    /// <param name="cancellationToken">A token that cancels the renewal before it is committed.</param>
    /// <returns>
    ///     <see langword="true" /> when the row was still processing under the supplied owner; otherwise
    ///     <see langword="false" />.
    /// </returns>
    Task<bool> RenewLeaseAsync(
        LeaseRenewalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Returns one leased message to the queue without charging the attempt its lease consumed.
    /// </summary>
    /// <param name="request">The lease release inputs.</param>
    /// <param name="cancellationToken">A token that cancels the release before it is committed.</param>
    /// <returns>
    ///     <see langword="true" /> when the row was still leased under the supplied owner and generation and was released;
    ///     otherwise <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Leasing a message increments its attempt count, so that a process that crashes mid-dispatch still spends
    ///         an attempt. A dispatch the processor cancels itself, during host shutdown or a drain, is not an attempt:
    ///         the processor calls this method so the row returns to <c>Pending</c>, or to <c>Failed</c> when it already
    ///         carries an error, with its lease cleared, its attempt count decremented, and its visibility unchanged.
    ///     </para>
    ///     <para>
    ///         The default implementation releases nothing and returns <see langword="false" />. The lease then expires
    ///         and the row is reclaimed with the attempt still counted, which is the behaviour of a store written before
    ///         this member existed.
    ///     </para>
    /// </remarks>
    Task<bool> ReleaseLeaseAsync(
        LeaseReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
