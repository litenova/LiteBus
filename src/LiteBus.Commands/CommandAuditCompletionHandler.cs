using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands;

/// <summary>
///     Writes an audit record when any command mediation ends.
/// </summary>
/// <remarks>
///     Registered by <see cref="CommandModuleBuilder.EnableAuditing" />. It runs at the completion stage, so a command
///     that was refused or that failed leaves a record just as a successful one does.
/// </remarks>
[HandlerPriority(HandlerPriorities.Observability)]
internal sealed class CommandAuditCompletionHandler : ICommandCompletionHandler
{
    /// <summary>
    ///     Produces and writes the audit record.
    /// </summary>
    private readonly IAuditRecordWriter _writer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandAuditCompletionHandler" /> class.
    /// </summary>
    /// <param name="writer">The writer that produces and persists audit records.</param>
    public CommandAuditCompletionHandler(IAuditRecordWriter writer)
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
