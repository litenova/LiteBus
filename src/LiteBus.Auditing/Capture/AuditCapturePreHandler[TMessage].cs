using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Capture;

/// <summary>
///     Resolves the capture mode for each audited mediation before its handler runs, and writes the intent record
///     for the BeforeAndOutcome mode.
/// </summary>
/// <remarks>
///     <para>
///         The completion stage observes only the built record, whose message type name cannot be resolved back to a
///         type, so the per-message capture override is resolved here, while the message type is still known, and
///         handed to the completion stage in the mediation data. A message that declares no audited position resolves
///         nothing and writes nothing.
///     </para>
///     <para>
///         Under <see cref="AuditCapture.BeforeAndOutcome" />, an intent record is written in its own transaction
///         before the handler runs, and the outcome record follows when the mediation ends. The intent record carries
///         <see cref="AuditOutcome.Intent" />: a record written before commit never claims success.
///     </para>
///     <para>
///         The pre-handler stage runs after guards, validators, and shortcuts, so a denied, invalid, or answered
///         message never reaches this handler. Those endings still leave their own record at the completion stage.
///     </para>
/// </remarks>
/// <typeparam name="TMessage">The message type this pre-handler runs for.</typeparam>
internal sealed class AuditCapturePreHandler<TMessage> : IMessagePreHandler<TMessage>
    where TMessage : notnull
{
    /// <summary>
    ///     The auditing configuration this pre-handler reads the builder default and the store capabilities from.
    /// </summary>
    private readonly AuditingOptions _options;

    /// <summary>
    ///     Resolves message descriptors so the audit declaration and the capture override can be read without
    ///     per-dispatch reflection.
    /// </summary>
    private readonly IMessageReader _reader;

    /// <summary>
    ///     Receives the intent record.
    /// </summary>
    private readonly IAuditTrail _trail;

    /// <summary>
    ///     Reads what an earlier pre-handler pushed for the record.
    /// </summary>
    private readonly IAuditScope _scope;

    /// <summary>
    ///     Supplies the time written to the intent record.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditCapturePreHandler{TMessage}" /> class.
    /// </summary>
    /// <param name="options">The auditing configuration.</param>
    /// <param name="reader">The message registry holding audit and capture declarations.</param>
    /// <param name="trail">The audit trail that receives the intent record.</param>
    /// <param name="scope">The audit scope an earlier pre-handler pushed to.</param>
    /// <param name="timeProvider">The time source for the intent record timestamp.</param>
    public AuditCapturePreHandler(
        AuditingOptions options,
        IMessageReader reader,
        IAuditTrail trail,
        IAuditScope scope,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(trail);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options;
        _reader = reader;
        _trail = trail;
        _scope = scope;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    /// <exception cref="NoExecutionContextException">No mediation is in flight.</exception>
    public async Task PreHandleAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = message.GetType();
        var descriptor = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy().Find(messageType, _reader);

        if (descriptor is null)
        {
            return;
        }

        if (!descriptor.Metadata.TryGet<AuditDeclaration>(out var declaration)
            || declaration is not AuditedDeclaration audited)
        {
            return;
        }

        var resolution = AuditCaptureResolver.Resolve(
            messageType,
            _reader,
            _options.Capture,
            _options.StoreCapabilities);
        var executionContext = AmbientExecutionContext.Current;

        AuditCaptureResolver.StoreResolvedCapture(executionContext, resolution.Effective);

        if (resolution.Effective is not AuditCapture.BeforeAndOutcomeCapture)
        {
            return;
        }

        var intent = BuildIntent(message, messageType, audited, executionContext);

        await _trail.WriteAsync(intent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds the intent record for a BeforeAndOutcome mediation.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="messageType">The concrete runtime message type.</param>
    /// <param name="declaration">The audit declaration recorded for the message.</param>
    /// <param name="executionContext">The mediation in flight.</param>
    /// <returns>The intent record, carrying <see cref="AuditOutcome.Intent" />.</returns>
    /// <remarks>
    ///     <para>
    ///         A missing required reason does not fail here. The completion stage enforces
    ///         <see cref="AuditedDeclaration.ReasonRequired" /> on the outcome record; failing the attempt record
    ///         would stop the handler from running, which changes what the outcome record may truthfully claim.
    ///     </para>
    ///     <para>
    ///         The registered <see cref="IAuditActorResolver" /> is not consulted: it resolves from a
    ///         <see cref="MessageCompletionContext" />, which does not exist until the mediation ends. Attribution
    ///         here comes from the scope and the calling principal; the outcome record carries the resolver's
    ///         actor.
    ///     </para>
    /// </remarks>
    private AuditRecord BuildIntent(
        object message,
        Type messageType,
        AuditedDeclaration declaration,
        IExecutionContext executionContext)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = declaration.Action,
            Actor = _scope.Actor ?? ResolveActorFromPrincipal(executionContext.Identity),
            Outcome = AuditOutcome.Intent,
            OccurredAt = _timeProvider.GetUtcNow(),
            Duration = TimeSpan.Zero,
            Category = declaration.Category,
            TargetKind = declaration.TargetKind,
            TargetId = _scope.TargetId ?? ResolveSelector(declaration.TargetSelector, message),
            Reason = _scope.Reason ?? ResolveSelector(declaration.ReasonSelector, message),
            FailureCode = null,
            MessageType = messageType.FullName,
            CorrelationId = ReadTraceItem(executionContext, MessageTraceContextKeys.CorrelationId),
            TenantId = ReadTraceItem(executionContext, MessageTraceContextKeys.TenantId),
            Properties = new Dictionary<string, string>(_scope.Properties, StringComparer.Ordinal)
        };
    }

    /// <summary>
    ///     Resolves the actor from the principal carried on the mediation identity.
    /// </summary>
    /// <param name="identity">The identity the mediation runs for.</param>
    /// <returns>The actor, or <see langword="null" /> when no principal or no usable identifier claim is present.</returns>
    /// <remarks>
    ///     The identifier is read from the <see cref="ClaimTypes.NameIdentifier" /> claim, falling back to the
    ///     <c>sub</c> claim, and reported as <see cref="AuditActor.User" /> with the display name read from the
    ///     <see cref="ClaimTypes.Name" /> claim. It mirrors the completion-stage fallback, so the intent record and
    ///     the outcome record attribute the attempt to the same actor.
    /// </remarks>
    private static AuditActor? ResolveActorFromPrincipal(ExecutionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var principal = identity.Principal;

        if (principal is null)
        {
            return null;
        }

        var id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(id))
        {
            id = principal.FindFirst("sub")?.Value;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var actor = AuditActor.User(id);
        var displayName = principal.FindFirst(ClaimTypes.Name)?.Value;

        return string.IsNullOrWhiteSpace(displayName) ? actor : actor with { DisplayName = displayName };
    }

    /// <summary>
    ///     Reads a declared value off the message, treating a missing or blank result as no value.
    /// </summary>
    /// <param name="selector">The declared projection, or <see langword="null" /> when the message declares none.</param>
    /// <param name="message">The message the selector reads.</param>
    /// <returns>The selected value, or <see langword="null" /> when the message carries none.</returns>
    /// <remarks>
    ///     A selector that throws is not caught here: a projection bug that silently produces evidence without its
    ///     target or justification is worse than a failed mediation.
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
    ///     Reads a trace value carried on the mediation in flight, when present.
    /// </summary>
    /// <param name="executionContext">The mediation in flight.</param>
    /// <param name="key">The execution-context item key to read.</param>
    /// <returns>The value, or <see langword="null" /> when absent.</returns>
    private static string? ReadTraceItem(IExecutionContext executionContext, string key)
    {
        return executionContext.Items.TryGetValue(key, out var value) ? value as string : null;
    }
}
