using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     Turns the end of a mediation into an audit record and hands it to the configured trail.
/// </summary>
/// <remarks>
///     <para>
///         This is the piece that makes the audit trail complete rather than a changelog. It runs at the completion
///         stage, which observes every ending, so a refusal and a failure leave a record just as a success does.
///     </para>
///     <para>
///         It reads the constant half of the record from the message's <see cref="AuditDeclaration" />, resolved once at
///         registration from an attribute or a definition facet. It reads the variable half from
///         <see cref="IAuditScope" />, which the handler populated while it ran.
///     </para>
/// </remarks>
public sealed class AuditRecordWriter
{
    /// <summary>
    ///     Classifies the mediation outcome in audit vocabulary.
    /// </summary>
    private readonly IAuditOutcomeMapper _outcomeMapper;

    /// <summary>
    ///     Resolves message descriptors so the audit declaration can be read without per-dispatch reflection.
    /// </summary>
    private readonly IMessageRegistry _registry;

    /// <summary>
    ///     Supplies the time written to the record.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Receives the produced records.
    /// </summary>
    private readonly IAuditTrail _trail;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditRecordWriter" /> class.
    /// </summary>
    /// <param name="trail">The trail that receives produced records.</param>
    /// <param name="registry">The message registry supplying declarative metadata.</param>
    /// <param name="outcomeMapper">The mapper that classifies the mediation outcome.</param>
    /// <param name="timeProvider">The time source for the record timestamp.</param>
    public AuditRecordWriter(
        IAuditTrail trail,
        IMessageRegistry registry,
        IAuditOutcomeMapper outcomeMapper,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(trail);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(outcomeMapper);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _trail = trail;
        _registry = registry;
        _outcomeMapper = outcomeMapper;
        _timeProvider = timeProvider;
    }

    /// <summary>
    ///     Writes an audit record for a completed mediation, when the message is declared as audited.
    /// </summary>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    public async Task WriteAsync(MessageCompletionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryResolveDeclaration(context.Message.GetType(), out var declaration) || !declaration.IsAudited)
        {
            return;
        }

        var scope = AmbientAuditScope.Find();
        var outcome = _outcomeMapper.Map(context);

        var record = new AuditRecord
        {
            Action = declaration.Action!,
            Outcome = outcome,
            OccurredAt = _timeProvider.GetUtcNow(),
            Duration = context.Duration,
            Category = declaration.Category,
            TargetKind = declaration.TargetKind,
            TargetId = scope?.TargetId,
            Reason = scope?.Reason ?? context.AbortReason,
            FailureCode = outcome == AuditOutcome.Succeeded ? null : _outcomeMapper.MapFailureCode(context),
            MessageType = context.Message.GetType().FullName,
            CorrelationId = ReadTraceItem(MessageTraceContextKeys.CorrelationId),
            TenantId = ReadTraceItem(MessageTraceContextKeys.TenantId),
            Properties = scope is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(scope.Properties, StringComparer.Ordinal)
        };

        await _trail.WriteAsync(record, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads a trace value carried on the ambient execution context, when present.
    /// </summary>
    /// <param name="key">The execution-context item key to read.</param>
    /// <returns>The value, or <see langword="null" /> when absent.</returns>
    private static string? ReadTraceItem(string key)
    {
        var executionContext = AmbientExecutionContext.GetCurrentOrDefault();

        if (executionContext is null)
        {
            return null;
        }

        return executionContext.Items.TryGetValue(key, out var value) ? value as string : null;
    }

    /// <summary>
    ///     Resolves the audit declaration recorded for a message type.
    /// </summary>
    /// <param name="messageType">The concrete runtime message type.</param>
    /// <param name="declaration">When this method returns <see langword="true" />, the resolved declaration.</param>
    /// <returns><see langword="true" /> when the message declares an audit position.</returns>
    private bool TryResolveDeclaration(Type messageType, out AuditDeclaration declaration)
    {
        var descriptor = _registry.Find(messageType);

        if (descriptor is null)
        {
            declaration = null!;
            return false;
        }

        // A definition facet stores the declaration directly. An attribute stores itself, so convert on read.
        if (descriptor.Metadata.TryGet<AuditDeclaration>(out var declared))
        {
            declaration = declared;
            return true;
        }

        if (descriptor.Metadata.TryGet<AuditedAttribute>(out var audited))
        {
            declaration = audited.ToDeclaration();
            return true;
        }

        if (descriptor.Metadata.TryGet<AuditExemptAttribute>(out var exempt))
        {
            declaration = exempt.ToDeclaration();
            return true;
        }

        declaration = null!;
        return false;
    }
}
