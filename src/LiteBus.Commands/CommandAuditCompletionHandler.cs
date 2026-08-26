using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;

namespace LiteBus.Commands;

/// <summary>
///     Writes an audit record when any command mediation ends.
/// </summary>
/// <remarks>
///     Registered by <see cref="CommandModuleBuilder.EnableAuditing" />. It runs at the completion stage, so a command
///     that was refused or that failed leaves a record just as a successful one does.
/// </remarks>
[HandlerPriority(LiteBusHandlerPriority.Observability)]
public sealed class CommandAuditCompletionHandler : ICommandCompletionHandler
{
    /// <summary>
    ///     Produces and writes the audit record.
    /// </summary>
    private readonly AuditRecordWriter _writer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandAuditCompletionHandler" /> class.
    /// </summary>
    /// <param name="writer">The writer that produces and persists audit records.</param>
    public CommandAuditCompletionHandler(AuditRecordWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<ICommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _writer.WriteAsync(context.AsUntyped(), cancellationToken);
    }
}
