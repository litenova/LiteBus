using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Abstractions.Exceptions;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     Dispatches outbox messages through the LiteBus in-process event mediator.
/// </summary>
/// <remarks>
///     <para>
///         This dispatcher is useful when the outbox should replay events into the local LiteBus event pipeline instead
///         of an external broker. It resolves the stored contract, deserializes the payload, then publishes the event
///         with the same event mediator semantics as an immediate <c>PublishAsync</c> call.
///     </para>
///     <para>
///         Events that implement <see cref="IEvent" /> are sent through the non-generic publisher overload. POCO events
///         are published through a closed generic helper cached per event type.
///     </para>
///     <para>
///         An envelope that records one handler publishes to that handler alone; an envelope without a recorded
///         handler publishes to every subscribed handler.
///     </para>
///     <para>
///         Each dispatch runs as a root mediation: the event is published inside <see cref="RootMediationScope" /> with
///         <see cref="EventMediationSettings.Cause" /> set from the envelope correlation and causation identifiers, so
///         processor delivery and immediate delivery observe the same lineage and the reaction never nests inside the
///         mediation that staged the envelope. The dispatcher also stamps one
///         <see cref="OutboxDispatchContext" /> into the mediation items, so handlers read the envelope attempt, the
///         cause, the identity, and the delivery path without ambient state.
///     </para>
///     <para>
///         A reaction that meets a storage conflict publishes again inside the same dispatch, bounded by
///         <see cref="OutboxReactionRetryOptions.MaxAttempts" /> with the delay
///         <see cref="IOutboxReactionRetryPolicy" /> advises per failure. A business refusal and a configuration
///         fault never consult the policy: the policy only decides over a classified conflict.
///     </para>
/// </remarks>
public sealed class EventOutboxDispatcher : IOutboxDispatcher
{
    /// <summary>
    ///     Caches closed generic publish delegates keyed by event type to avoid repeated reflection overhead.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<IEventMediator, object, EventMediationSettings?, CancellationToken, Task>> PublishDelegateCache = new();

    /// <summary>
    ///     The open generic publish helper resolved once at type initialization.
    /// </summary>
    private static readonly MethodInfo PublishTypedAsyncMethod = typeof(EventOutboxDispatcher)
                                                                     .GetMethod(nameof(PublishTypedAsync), BindingFlags.NonPublic | BindingFlags.Static) ??
                                                                 throw new InvalidOperationException($"Could not resolve {nameof(PublishTypedAsync)}.");

    /// <summary>
    ///     Gets the registry used to resolve persisted contracts back to event types.
    /// </summary>
    private readonly IContractReader _contractRegistry;

    /// <summary>
    ///     Gets the LiteBus event publisher used as the dispatch target.
    /// </summary>
    private readonly IEventMediator _eventPublisher;

    /// <summary>
    ///     Gets the serializer used to hydrate the persisted payload.
    /// </summary>
    private readonly IMessageSerializer _messageSerializer;

    /// <summary>
    ///     Gets the optional outbox protector used to decrypt stored payloads before deserialization.
    /// </summary>
    private readonly IOutboxPayloadProtector? _payloadProtector;

    /// <summary>
    ///     Gets the dispatch options that decide how a message with no handler is treated.
    /// </summary>
    private readonly InProcessOutboxDispatchOptions _options;

    /// <summary>
    ///     Gets the factory used to create the scope a retry policy evaluation resolves from, or
    ///     <see langword="null" /> when the dispatcher was constructed without one.
    /// </summary>
    private readonly IMessageDispatchScopeFactory? _dispatchScopeFactory;

    /// <summary>
    ///     Gets the clock retry delays are waited against.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the bound and delay curve for in-dispatch retries of conflicted reactions.
    /// </summary>
    private readonly OutboxReactionRetryOptions _reactionRetryOptions;

    /// <summary>
    ///     Gets the logger used for reaction retry diagnostics.
    /// </summary>
    private readonly ILogger<EventOutboxDispatcher> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventOutboxDispatcher" /> class.
    /// </summary>
    /// <param name="eventPublisher">The LiteBus event publisher used as the dispatch target.</param>
    /// <param name="contractRegistry">The registry used to resolve persisted contracts back to event types.</param>
    /// <param name="messageSerializer">The serializer used to hydrate the persisted payload.</param>
    /// <param name="payloadProtector">The optional outbox protector used to decrypt stored payloads before deserialization.</param>
    /// <param name="options">The dispatch options, or null to require a handler for every dispatched message.</param>
    /// <param name="dispatchScopeFactory">
    ///     The factory used to create the scope a retry policy evaluation resolves from, or <see langword="null" />
    ///     to evaluate the default policy without a scope.
    /// </param>
    /// <param name="clock">The clock retry delays are waited against, or <see langword="null" /> for the system clock.</param>
    /// <param name="reactionRetryOptions">The bound and delay curve for in-dispatch retries, or <see langword="null" /> for the defaults.</param>
    /// <param name="logger">The logger used for reaction retry diagnostics, or <see langword="null" /> to discard it.</param>
    public EventOutboxDispatcher(
        IEventMediator eventPublisher,
        IContractReader contractRegistry,
        IMessageSerializer messageSerializer,
        IOutboxPayloadProtector? payloadProtector = null,
        InProcessOutboxDispatchOptions? options = null,
        IMessageDispatchScopeFactory? dispatchScopeFactory = null,
        TimeProvider? clock = null,
        OutboxReactionRetryOptions? reactionRetryOptions = null,
        ILogger<EventOutboxDispatcher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(eventPublisher);

        _eventPublisher = eventPublisher;
        ArgumentNullException.ThrowIfNull(contractRegistry);

        _contractRegistry = contractRegistry;
        ArgumentNullException.ThrowIfNull(messageSerializer);

        _messageSerializer = messageSerializer;
        _payloadProtector = payloadProtector;
        _options = options ?? new InProcessOutboxDispatchOptions();
        _dispatchScopeFactory = dispatchScopeFactory;
        _clock = clock ?? TimeProvider.System;
        _reactionRetryOptions = reactionRetryOptions ?? new OutboxReactionRetryOptions();
        _logger = logger ?? NullLogger<EventOutboxDispatcher>.Instance;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A direct call without a delivery path reports the background processor kind, which is the path that
    ///     dispatches every envelope the immediate listener does not claim.
    /// </remarks>
    public Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        return DispatchAsync(message, OutboxDispatchKind.Background, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The final <see cref="Exception" /> handler covers any serialization, contract, mediation, or handler failure
    ///     because the dispatch boundary must map every failure to <see cref="LiteBusDispatchException" /> for the
    ///     processor retry policy. Caller-requested cancellation is rethrown unwrapped.
    /// </remarks>
    public async Task DispatchAsync(
        OutboxEnvelope message,
        OutboxDispatchKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var messageType = _contractRegistry.GetMessageType(message.ContractName, message.ContractVersion);

            var payload = await PayloadProtection.UnprotectAsync(
                    message.Payload,
                    _payloadProtector,
                    new PayloadProtectionContext
                    {
                        MessageId = message.Id,
                        ContractName = message.ContractName,
                        ContractVersion = message.ContractVersion,
                        TenantId = message.TenantId,
                        Axis = "outbox"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            var messageInstance = await _messageSerializer.DeserializeAsync(messageType, payload, cancellationToken).ConfigureAwait(false);
            var dispatch = CreateDispatchContext(message, kind);
            var mediationSettings = CreateMediationSettings(message, dispatch, _options);

            await PublishWithReactionRetryAsync(
                    message,
                    messageType,
                    messageInstance,
                    mediationSettings,
                    dispatch,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (LiteBusDispatchException)
        {
            throw;
        }

        // The caller's cancellation is a decision, not a dispatch failure. Wrapping it would hide it from the envelope
        // handler, which then charges a shutdown or a drain to the message's retry budget.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Dispatch boundary maps any serialization or handler failure to LiteBusDispatchException.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            throw new LiteBusDispatchException(CreateFailureMessage(message, exception), exception);
        }
    }

    /// <summary>
    ///     Publishes one reaction, publishing again inside the same dispatch while a storage conflict meets a retry.
    /// </summary>
    /// <param name="message">The outbox envelope being dispatched.</param>
    /// <param name="messageType">The runtime message type resolved from the outbox contract.</param>
    /// <param name="messageInstance">The deserialized message instance.</param>
    /// <param name="mediationSettings">The mediation settings copied from the outbox envelope.</param>
    /// <param name="dispatch">The delivery the attempts belong to.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch before the target observes the message.</param>
    /// <returns>A task that completes when the reaction publishes or its dispatch fails terminally.</returns>
    /// <exception cref="LiteBusDispatchException">The reaction failed without a further in-dispatch retry.</exception>
    /// <remarks>
    ///     Each attempt publishes as a new root mediation with the same cause, so a retry never nests inside the
    ///     failed attempt. A failure that is not a classified storage conflict, a conflict the bound already spent,
    ///     and a conflict the policy declines all end the dispatch here; the envelope then follows the ordinary
    ///     processor retry schedule instead of losing the message.
    /// </remarks>
    private async Task PublishWithReactionRetryAsync(
        OutboxEnvelope message,
        Type messageType,
        object messageInstance,
        EventMediationSettings mediationSettings,
        OutboxDispatchContext dispatch,
        CancellationToken cancellationToken)
    {
        for (var reactionAttempt = 1; ; reactionAttempt++)
        {
            try
            {
                using (RootMediationScope.Begin())
                {
                    if (messageInstance is IEvent liteBusEvent)
                    {
                        await _eventPublisher.PublishAsync(liteBusEvent, mediationSettings, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    var publish = PublishDelegateCache.GetOrAdd(messageType, CreatePublishDelegate);
                    await publish(_eventPublisher, messageInstance, mediationSettings, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (LiteBusDispatchException)
            {
                throw;
            }
#pragma warning disable CA1031 // Publish boundary captures any handler failure so the retry decision sees the conflict it classifies.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                if (!IsRetryableConflict(exception)
                    || !_reactionRetryOptions.Enabled
                    || reactionAttempt >= _reactionRetryOptions.MaxAttempts)
                {
                    if (IsRetryableConflict(exception) && _reactionRetryOptions.Enabled)
                    {
                        OutboxReactionRetryTelemetry.RecordExhausted();
                        InProcessOutboxDispatchLogMessages.ReactionRetriesExhausted(_logger, message.Id, reactionAttempt);
                    }

                    throw new LiteBusDispatchException(CreateFailureMessage(message, exception), exception);
                }

                var decision = await DecideAsync(
                        message,
                        messageInstance,
                        messageType,
                        dispatch,
                        exception,
                        reactionAttempt,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (decision is not OutboxReactionRetryDecision.Retry retry)
                {
                    OutboxReactionRetryTelemetry.RecordExhausted();
                    throw new LiteBusDispatchException(CreateFailureMessage(message, exception), exception);
                }

                var delay = ClampDelay(retry.Delay);

                OutboxReactionRetryTelemetry.RecordRetry();
                InProcessOutboxDispatchLogMessages.ReactionRetryScheduled(_logger, message.Id, reactionAttempt, delay);

                await Task.Delay(delay, _clock, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Asks the reaction retry policy whether a conflicted attempt publishes again.
    /// </summary>
    /// <param name="message">The outbox envelope being dispatched.</param>
    /// <param name="messageInstance">The deserialized message instance.</param>
    /// <param name="messageType">The runtime message type resolved from the outbox contract.</param>
    /// <param name="dispatch">The delivery the failed attempt belongs to.</param>
    /// <param name="failure">The exception that ended the attempt.</param>
    /// <param name="reactionAttempt">The 1-based in-dispatch attempt that just failed.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch before the target observes the message.</param>
    /// <returns>
    ///     The policy decision, or <see langword="null" /> when the policy threw and the dispatch must end without
    ///     retrying.
    /// </returns>
    /// <remarks>
    ///     The policy resolves from a fresh dispatch scope per conflict, so a replacement observes the same scoped
    ///     services the handlers do and a changed registration applies without rebuilding the composition. A policy
    ///     that throws is logged with the envelope context and treated as a declined retry: the conflicted envelope
    ///     still follows the ordinary processor schedule instead of losing the message to a faulty port.
    /// </remarks>
    private async Task<OutboxReactionRetryDecision?> DecideAsync(
        OutboxEnvelope message,
        object messageInstance,
        Type messageType,
        OutboxDispatchContext dispatch,
        Exception failure,
        int reactionAttempt,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_dispatchScopeFactory is null)
            {
                var fallback = new DefaultOutboxReactionRetryPolicy(_reactionRetryOptions);
                var fallbackEvaluation = new MessageEvaluationContext(
                    messageInstance,
                    messageType,
                    dispatch.Identity,
                    EmptyServiceProvider.Instance,
                    _clock,
                    _clock.GetUtcNow());

                return await fallback.DecideAsync(
                        new OutboxReactionRetryContext(fallbackEvaluation, dispatch, failure, reactionAttempt),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var scope = _dispatchScopeFactory.CreateScope();
            await using var configuredScope = scope.ConfigureAwait(false);

            var policy = scope.ServiceProvider.GetService(typeof(IOutboxReactionRetryPolicy)) as IOutboxReactionRetryPolicy
                ?? new DefaultOutboxReactionRetryPolicy(_reactionRetryOptions);

            var evaluation = new MessageEvaluationContext(
                messageInstance,
                messageType,
                dispatch.Identity,
                scope.ServiceProvider,
                _clock,
                _clock.GetUtcNow());

            return await policy.DecideAsync(
                    new OutboxReactionRetryContext(evaluation, dispatch, failure, reactionAttempt),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A throwing policy is logged and treated as a declined retry so a faulty port cannot lose a message.
        catch (Exception policyException)
#pragma warning restore CA1031
        {
            InProcessOutboxDispatchLogMessages.ReactionRetryPolicyFailed(_logger, message.Id, policyException);
            return null;
        }
    }

    /// <summary>
    ///     Determines whether a publish failure is a storage conflict the retry policy may decide over.
    /// </summary>
    /// <param name="exception">The exception that ended the attempt.</param>
    /// <returns>
    ///     <see langword="true" /> for a classified conflict that carries no configuration fault; otherwise,
    ///     <see langword="false" /> for a refusal without a conflict cause, a configuration fault, and every other
    ///     failure.
    /// </returns>
    /// <remarks>
    ///     The classification walks the whole exception graph, so a conflict a refusal classifier translated into a
    ///     typed refusal still shows the storage fault the refusal keeps as its cause, and that fault is what the
    ///     policy retries. A chain carrying <see cref="MessageFaultException" /> is excluded: a configuration fault
    ///     ends the dispatch unchanged instead of spending retries on an answer that cannot change.
    /// </remarks>
    private static bool IsRetryableConflict(Exception exception)
    {
        return MediationExceptionFilters.IsConcurrencyConflict(exception) && !ContainsMessageFault(exception);
    }

    /// <summary>
    ///     Determines whether an exception graph carries a configuration fault.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns><see langword="true" /> when the graph carries a <see cref="MessageFaultException" />.</returns>
    private static bool ContainsMessageFault(Exception exception)
    {
        if (exception is MessageFaultException)
        {
            return true;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                if (ContainsMessageFault(inner))
                {
                    return true;
                }
            }

            return false;
        }

        return exception.InnerException is not null && ContainsMessageFault(exception.InnerException);
    }

    /// <summary>
    ///     Bounds a policy-advised delay at zero from below.
    /// </summary>
    /// <param name="delay">The delay the policy advised.</param>
    /// <returns>The delay, or <see cref="TimeSpan.Zero" /> when the policy advised a negative one.</returns>
    private static TimeSpan ClampDelay(TimeSpan delay)
    {
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    /// <summary>
    ///     Builds the operator-facing message for a failed dispatch.
    /// </summary>
    /// <param name="message">The outbox envelope that failed to dispatch.</param>
    /// <param name="exception">The exception that ended the dispatch.</param>
    /// <returns>The failure message, with registration guidance only when the cause is a registration problem.</returns>
    /// <remarks>
    ///     Registration guidance on an unrelated failure, such as a concurrency conflict raised by a handler, sends the
    ///     reader after a configuration bug that does not exist, so it is added only for the failures it explains.
    /// </remarks>
    private static string CreateFailureMessage(OutboxEnvelope message, Exception exception)
    {
        var summary =
            $"Outbox dispatch failed for contract '{message.ContractName}' version {message.ContractVersion}: " +
            $"{exception.GetType().Name}: {exception.Message}";

        if (exception is not (MessageContractNotRegisteredException or NoHandlerFoundException))
        {
            return summary;
        }

        return summary +
               " Verify the event type is registered with Contracts.Register, implements IEvent or is a supported POCO " +
               "event, and that AddEventModule registered handlers for the event. When the event handlers live in an " +
               "assembly AddEvents does not scan, the message is dispatched to nothing; call AllowEnvelopesWithoutHandler " +
               "on UseInProcessDispatch only when publishing to zero handlers is intended.";
    }

    /// <summary>
    ///     Publishes a POCO event through the generic <see cref="IEventMediator.PublishAsync{TEvent}" /> overload.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type closed from the persisted contract.</typeparam>
    /// <param name="eventPublisher">The event mediator used as the dispatch target.</param>
    /// <param name="messageInstance">The deserialized message instance.</param>
    /// <param name="mediationSettings">The mediation settings copied from the outbox envelope.</param>
    /// <param name="cancellationToken">The token used to cancel publication.</param>
    /// <returns>A task that completes when publication finishes.</returns>
    private static Task PublishTypedAsync<TEvent>(
        IEventMediator eventPublisher,
        object messageInstance,
        EventMediationSettings? mediationSettings,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        return eventPublisher.PublishAsync((TEvent) messageInstance, mediationSettings, cancellationToken);
    }

    /// <summary>
    ///     Creates a closed generic publish delegate for the supplied event type.
    /// </summary>
    /// <param name="messageType">The runtime message type resolved from the outbox contract.</param>
    /// <returns>A delegate that publishes through <see cref="IEventMediator.PublishAsync{TEvent}" />.</returns>
    /// <exception cref="LiteBusDispatchException">The publish delegate could not be created for the message type.</exception>
    /// <remarks>
    ///     The delegate is built with <c>CreateDelegate</c> rather than <c>MethodInfo.Invoke</c> so handler failures
    ///     surface unwrapped and registration guidance still matches. The final <see cref="Exception" /> handler covers
    ///     any reflection failure from <c>MakeGenericMethod</c> or delegate creation because the dispatch boundary must
    ///     report an unusable event type as <see cref="LiteBusDispatchException" /> with registration guidance.
    /// </remarks>
    private static Func<IEventMediator, object, EventMediationSettings?, CancellationToken, Task> CreatePublishDelegate(Type messageType)
    {
        try
        {
            var closedMethod = PublishTypedAsyncMethod.MakeGenericMethod(messageType);

            return closedMethod.CreateDelegate<Func<IEventMediator, object, EventMediationSettings?, CancellationToken, Task>>();
        }
#pragma warning disable CA1031 // Reflection over any event type can fail; the dispatch boundary reports it as LiteBusDispatchException.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            throw new LiteBusDispatchException(
                $"Outbox dispatch could not create a publish delegate for message type '{messageType.FullName}'. " +
                "Register the closed event type with Contracts.Register and ensure it is a valid POCO or IEvent implementation.",
                exception);
        }
    }

    /// <summary>
    ///     Creates the dispatch context describing one delivery for handlers and retry policies.
    /// </summary>
    /// <param name="message">The outbox message being dispatched.</param>
    /// <param name="kind">The delivery path that started this dispatch.</param>
    /// <returns>The dispatch context stamped into the mediation items.</returns>
    private static OutboxDispatchContext CreateDispatchContext(OutboxEnvelope message, OutboxDispatchKind kind)
    {
        return new OutboxDispatchContext(
            message.Id,
            message.ContractName,
            message.AttemptCount,
            CreateMediationCause(message),
            ExecutionIdentity.ForTenant(message.TenantId),
            kind);
    }

    /// <summary>
    ///     Creates event mediation settings with trace metadata, tenant identity, and mediation cause copied from the
    ///     outbox envelope.
    /// </summary>
    /// <param name="message">The outbox message whose correlation, causation, and tenant values should be applied.</param>
    /// <param name="dispatch">The dispatch context stamped into the mediation items.</param>
    /// <param name="options">The dispatch options that decide how a message with no handler is treated.</param>
    /// <returns>Event mediation settings configured for outbox replay.</returns>
    /// <remarks>
    ///     An envelope addressed to one handler by per-handler delivery publishes with a handler predicate matching
    ///     only that main handler, so the reaction retries alone while pipeline observers still run. When the recorded
    ///     handler no longer subscribes, the predicate matches no main handler and
    ///     <see cref="InProcessOutboxDispatchOptions.RequireHandler" /> decides whether the attempt fails or completes
    ///     as published. The mediation runs in the tenant recorded on the envelope, restored through
    ///     <see cref="ExecutionIdentity.ForTenant" /> without the calling principal, which is never persisted. The
    ///     mediation cause carries the envelope correlation and causation identifiers with an outbox source, so the
    ///     reaction runs as a root mediation with typed lineage.
    /// </remarks>
    private static EventMediationSettings CreateMediationSettings(
        OutboxEnvelope message,
        OutboxDispatchContext dispatch,
        InProcessOutboxDispatchOptions options)
    {
        var settings = new EventMediationSettings
        {
            ThrowIfNoHandlerFound = options.RequireHandler,
            Routing = CreateRoutingSettings(message),
            Identity = dispatch.Identity,
            Cause = dispatch.Cause
        };

        settings.Items[OutboxDispatchContext.MediationItemsKey] = dispatch;

        MessageProcessorDiagnostics.ApplyTraceMetadata(
            settings.Items,
            message.CorrelationId,
            message.CausationId,
            message.TenantId,
            message.TraceContext);

        return settings;
    }

    /// <summary>
    ///     Creates the mediation cause identifying the committed outbox envelope that caused the reaction.
    /// </summary>
    /// <param name="message">The outbox message being dispatched.</param>
    /// <returns>The cause with the envelope correlation and causation identifiers and an outbox source.</returns>
    /// <remarks>
    ///     An identifier the envelope does not carry falls back to the envelope identifier, so a dispatched reaction
    ///     always carries a cause: the envelope itself is the committed fact, and an unstamped envelope starts its own
    ///     workflow. Processor delivery and immediate delivery dispatch the same envelope through this dispatcher, so
    ///     both observe the same cause.
    /// </remarks>
    private static MediationCause CreateMediationCause(OutboxEnvelope message)
    {
        var fallback = message.Id.ToString("D");

        return new MediationCause(
            string.IsNullOrWhiteSpace(message.CorrelationId) ? fallback : message.CorrelationId,
            string.IsNullOrWhiteSpace(message.CausationId) ? fallback : message.CausationId,
            MediationCauseSource.Outbox);
    }

    /// <summary>
    ///     Creates routing settings that restrict publication to the recorded handler for a per-handler envelope.
    /// </summary>
    /// <param name="message">The outbox message being dispatched.</param>
    /// <returns>Routing settings matching only the recorded handler, or default routing for a broadcast envelope.</returns>
    /// <remarks>
    ///     The predicate restricts main handlers only. Pre-stage, post, error, and completion observers observe every
    ///     mediation by message type rather than by main handler, so they pass through and run on each targeted
    ///     dispatch exactly as they would on a broadcast. The recorded value matches by subscriber name or by full
    ///     type name, so an envelope written before the handler adopted
    ///     <see cref="SubscriberNameAttribute" /> still reaches it.
    /// </remarks>
    private static EventRoutingSettings CreateRoutingSettings(OutboxEnvelope message)
    {
        if (message.Handler is null)
        {
            return new EventRoutingSettings();
        }

        var handler = message.Handler;

        return new EventRoutingSettings
        {
            HandlerPredicate = descriptor => descriptor is not IMainHandlerDescriptor ||
                SubscriberNames.Matches(descriptor.HandlerType, handler)
        };
    }
}
