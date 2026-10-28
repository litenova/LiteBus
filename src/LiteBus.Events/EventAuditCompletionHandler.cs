using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events;

/// <summary>
///     Writes an audit record when any event mediation ends.
/// </summary>
/// <remarks>
///     <para>
///         Registered by <see cref="EventModuleBuilder.EnableAuditing" />. A domain fact is often the most
///         audit-worthy thing in a system, and the event axis had no switch for it while commands and queries did.
///     </para>
///     <para>
///         One record per publish, not per handler. The mediation is the unit being audited, and the broadcast strategy
///         reports one outcome for the whole publish, so a record per subscriber would multiply one fact into as many
///         entries as there happen to be reactions and would change count whenever a handler is added. A single
///         handler failing inside a broadcast is a handler concern, visible through the error stage and through the
///         failed outcome on this record.
///     </para>
/// </remarks>
[HandlerPriority(HandlerPriorities.Observability)]
internal sealed class EventAuditCompletionHandler : IEventCompletionHandler
{
    /// <summary>
    ///     Produces and writes the audit record.
    /// </summary>
    private readonly IAuditRecordWriter _writer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventAuditCompletionHandler" /> class.
    /// </summary>
    /// <param name="writer">The writer that produces and persists audit records.</param>
    public EventAuditCompletionHandler(IAuditRecordWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<IEvent> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _writer.WriteAsync(context.AsUntyped(), cancellationToken);
    }
}
