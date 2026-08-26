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
///         Two properties are worth preserving in an implementation. A record for a successful action should be written
///         in the same transaction as the change it describes, so an action cannot exist without its record. A record
///         for a denial or a failure cannot ride that transaction, because the transaction is the one being rolled back,
///         so it has to be written out of band and must survive the failure that caused it.
///     </para>
///     <para>
///         Do not publish the trail through the outbox. The outbox provides at-least-once delivery to other systems,
///         while evidence needs durability at the source. Ship a copy to a SIEM through the outbox if you want, but
///         write the trail itself synchronously.
///     </para>
/// </remarks>
public interface IAuditTrail
{
    /// <summary>
    ///     Writes one audit record.
    /// </summary>
    /// <param name="record">The record produced at the mediation boundary.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken);
}
