using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries;

/// <summary>
///     Writes an audit record when any query mediation ends.
/// </summary>
/// <remarks>
///     Registered by <see cref="QueryModuleBuilder.EnableAuditing" />. Reads are audited for the same reason writes are:
///     a review asks who looked at sensitive data, and a refused read is the entry that matters most.
/// </remarks>
[HandlerPriority(HandlerPriorities.Observability)]
internal sealed class QueryAuditCompletionHandler : IQueryCompletionHandler
{
    /// <summary>
    ///     Produces and writes the audit record.
    /// </summary>
    private readonly IAuditRecordWriter _writer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="QueryAuditCompletionHandler" /> class.
    /// </summary>
    /// <param name="writer">The writer that produces and persists audit records.</param>
    public QueryAuditCompletionHandler(IAuditRecordWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<IQuery> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _writer.WriteAsync(context.AsUntyped(), cancellationToken);
    }
}
