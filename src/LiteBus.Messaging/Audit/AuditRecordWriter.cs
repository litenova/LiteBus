using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

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
///         registration from an attribute or a definition. It reads the variable half from <see cref="IAuditScope" />,
///         which the handler populated while it ran.
///     </para>
/// </remarks>
internal sealed class AuditRecordWriter : IAuditRecordWriter
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

    /// <inheritdoc />
    public async Task WriteAsync(MessageCompletionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messageType = context.Message.GetType();

        if (ResolveDeclaration(messageType) is not AuditedDeclaration declaration)
        {
            return;
        }

        var scope = AmbientAuditScope.Find();
        var outcome = _outcomeMapper.Map(context);
        var reason = scope?.Reason ?? context.Reason;

        if (declaration.ReasonRequired && outcome == AuditOutcome.Succeeded && string.IsNullOrWhiteSpace(reason))
        {
            throw new LiteBusConfigurationException(
                $"The action '{declaration.Action}' declares that a reason is required, but the handler for "
                + $"'{messageType.Name}' supplied none. Call IAuditScope.WithReason before the handler returns, or drop "
                + "ReasonRequired from the declaration.");
        }

        var record = new AuditRecord
        {
            Action = declaration.Action,
            Outcome = outcome,
            OccurredAt = _timeProvider.GetUtcNow(),
            Duration = context.Duration,
            Category = declaration.Category,
            TargetKind = declaration.TargetKind,
            TargetId = scope?.TargetId,
            Reason = reason,
            FailureCode = _outcomeMapper.MapFailureCode(context),
            MessageType = messageType.FullName,
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
    /// <returns>The declaration, or <see langword="null" /> when the message declares no audit position.</returns>
    /// <remarks>
    ///     Attributes and definitions both contribute an <see cref="AuditDeclaration" /> during registration, so this is
    ///     one lookup by one key rather than a search through the sources.
    /// </remarks>
    private AuditDeclaration? ResolveDeclaration(Type messageType)
    {
        var descriptor = _registry.Find(messageType);

        if (descriptor is null)
        {
            return null;
        }

        return descriptor.Metadata.TryGet<AuditDeclaration>(out var declaration) ? declaration : null;
    }
}
