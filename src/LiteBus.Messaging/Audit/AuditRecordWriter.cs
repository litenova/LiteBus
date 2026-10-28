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
///         registration from an attribute or a definition. It reads the variable half from <see cref="IAuditScope" />,
///         which the handler populated while it ran, falling back to the declaration's selectors where the scope is
///         silent and the value can be read off the message itself.
///     </para>
/// </remarks>
internal sealed class AuditRecordWriter : IAuditRecordWriter
{
    /// <summary>
    ///     Resolves who the action is attributed to, or null when the application registered no resolver.
    /// </summary>
    private readonly IAuditActorResolver? _actorResolver;

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
    /// <param name="actorResolver">
    ///     The resolver that says who acted, or <see langword="null" /> when the application registered none. A trail
    ///     with no actor is still written, because a record naming nobody is more useful than no record at all, and
    ///     the <c>litebus.audit.trail</c> probe reports the gap at startup.
    /// </param>
    public AuditRecordWriter(
        IAuditTrail trail,
        IMessageRegistry registry,
        IAuditOutcomeMapper outcomeMapper,
        TimeProvider timeProvider,
        IAuditActorResolver? actorResolver = null)
    {
        ArgumentNullException.ThrowIfNull(trail);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(outcomeMapper);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _trail = trail;
        _registry = registry;
        _outcomeMapper = outcomeMapper;
        _timeProvider = timeProvider;
        _actorResolver = actorResolver;
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
        var reason = scope?.Reason ?? context.Reason ?? ResolveSelector(declaration.ReasonSelector, context.Message);

        if (declaration.ReasonRequired && outcome == AuditOutcome.Succeeded && string.IsNullOrWhiteSpace(reason))
        {
            throw new AuditReasonMissingException(declaration.Action, messageType);
        }

        var record = new AuditRecord
        {
            Action = declaration.Action,
            Actor = ResolveActor(context, scope),
            Outcome = outcome,
            OccurredAt = _timeProvider.GetUtcNow(),
            Duration = context.Duration,
            Category = declaration.Category,
            TargetKind = declaration.TargetKind,
            TargetId = scope?.TargetId ?? ResolveSelector(declaration.TargetSelector, context.Message),
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
    ///     Resolves who the action is attributed to.
    /// </summary>
    /// <param name="context">How the mediation ended, carrying the message the resolver reads.</param>
    /// <param name="scope">The scope state the handler pushed, when it pushed any.</param>
    /// <returns>The actor, or <see langword="null" /> when nothing established one.</returns>
    /// <remarks>
    ///     The handler wins over the resolver. A resolver states the rule that holds for every message, and a handler
    ///     that called <see cref="IAuditScope.WithActor" /> knows something the rule does not, so overriding is the
    ///     only precedence that makes the call worth having. A resolver that throws is not caught here: an attribution
    ///     bug that silently produces unattributed evidence is worse than a failed mediation.
    /// </remarks>
    private AuditActor? ResolveActor(MessageCompletionContext context, AuditScopeState? scope)
    {
        return scope?.Actor ?? _actorResolver?.Resolve(context);
    }

    /// <summary>
    ///     Reads a declared value off the message, treating a missing or blank result as no value.
    /// </summary>
    /// <param name="selector">The declared projection, or <see langword="null" /> when the message declares none.</param>
    /// <param name="message">The message the selector reads.</param>
    /// <returns>The selected value, or <see langword="null" /> when the message carries none.</returns>
    /// <remarks>
    ///     A selector that throws is not caught here, for the same reason a throwing
    ///     <see cref="IAuditActorResolver" /> is not: a projection bug that silently produces evidence without its
    ///     target or justification is worse than a failed mediation. The scope and, for the reason, the pipeline
    ///     decision are consulted first, so the selector runs only when both are silent, and its exception fails the
    ///     record write the way any other writer failure does.
    /// </remarks>
    private static string? ResolveSelector(Func<object, string?>? selector, object message)
    {
        if (selector is null)
        {
            return null;
        }

        var selected = selector(message);
        return string.IsNullOrWhiteSpace(selected) ? null : selected;
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
    ///     one lookup by one key rather than a search through the sources. The declaration is read through the
    ///     descriptor the mediation resolved: an unregistered runtime type mediated through a base descriptor is
    ///     audited under that descriptor's declaration, exactly as if the type had been registered, so whether a
    ///     mediation leaves a record never depends on scan coverage.
    /// </remarks>
    private AuditDeclaration? ResolveDeclaration(Type messageType)
    {
        var descriptor = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy().Find(messageType, _registry);

        if (descriptor is null)
        {
            return null;
        }

        return descriptor.Metadata.TryGet<AuditDeclaration>(out var declaration) ? declaration : null;
    }
}
