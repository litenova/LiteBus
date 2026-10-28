using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Turns the end of a mediation into an audit record and hands it to the configured <see cref="IAuditTrail" />.
/// </summary>
/// <remarks>
///     <para>
///         This is the seam the per-axis audit handlers use, so the record is produced the same way for a command and for
///         a query. It reads the constant half of the record from the message's <see cref="AuditDeclaration" />, resolved
///         once at registration, and the variable half from <see cref="IAuditScope" />, which the handler populated while
///         it ran.
///     </para>
///     <para>
///         LiteBus registers an implementation when an axis enables auditing. Replace it only to change how a record is
///         composed; to change where records go, implement <see cref="IAuditTrail" /> instead.
///     </para>
/// </remarks>
public interface IAuditRecordWriter
{
    /// <summary>
    ///     Writes an audit record for a completed mediation, when the message is declared as audited.
    /// </summary>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <param name="cancellationToken">The cancellation token passed to the completion stage.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    Task WriteAsync(MessageCompletionContext context, CancellationToken cancellationToken = default);
}
