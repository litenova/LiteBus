using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries;

/// <summary>
///     Writes an audit record when any query mediation ends.
/// </summary>
/// <remarks>
///     Registered by <see cref="QueryModuleBuilder.EnableAuditing" />. Auditing reads matters for queries that return
///     personal data or that export in bulk; declare those with <see cref="AuditedAttribute" /> or an audit definition
///     and leave the rest exempt.
/// </remarks>
[HandlerPriority(LiteBusHandlerPriority.Observability)]
public sealed class QueryAuditCompletionHandler : IQueryCompletionHandler
{
    /// <summary>
    ///     Produces and writes the audit record.
    /// </summary>
    private readonly AuditRecordWriter _writer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="QueryAuditCompletionHandler" /> class.
    /// </summary>
    /// <param name="writer">The writer that produces and persists audit records.</param>
    public QueryAuditCompletionHandler(AuditRecordWriter writer)
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
