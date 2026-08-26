using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Receives audit records produced at the mediation boundary.
/// </summary>
/// <remarks>
///     <para>
///         LiteBus decides when a record is produced and what it contains. Where it is written, and with what durability
///         and integrity guarantees, is the application's decision, so this contract is deliberately small.
///     </para>
///     <para>
///         The record arrives at the completion stage, which runs after the main handler and after post-handlers. By then
///         a unit of work opened inside the pipeline has usually committed, so a trail that needs a record to share the
///         transaction of the change it describes cannot write straight through: buffer the record in the unit of work
///         and let the commit flush it. LiteBus does not buffer on your behalf, because only the application knows what
///         its transaction boundary is.
///     </para>
///     <para>
///         A record for a denial or a failure cannot ride that transaction in any case, because the transaction is the
///         one being rolled back. Write those out of band, and make sure the write survives the failure that caused it.
///     </para>
///     <para>
///         Do not publish the trail through the outbox. The outbox provides at-least-once delivery to other systems,
///         while evidence needs durability at the source. Ship a copy to a SIEM through the outbox if you want, but
///         write the trail itself synchronously.
///     </para>
///     <para>
///         The completion stage is not cancellable, so the token passed to <see cref="WriteAsync" /> is
///         <see cref="CancellationToken.None" />. A cancelled mediation still produces its record; that is the point.
///     </para>
/// </remarks>
public interface IAuditTrail
{
    /// <summary>
    ///     Writes one audit record.
    /// </summary>
    /// <param name="record">The record produced at the mediation boundary.</param>
    /// <param name="cancellationToken">
    ///     The cancellation token for the write. The completion stage is not cancellable, so this is
    ///     <see cref="CancellationToken.None" />; apply your own deadline if the write needs one.
    /// </param>
    /// <returns>A task representing the asynchronous write.</returns>
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default);
}
