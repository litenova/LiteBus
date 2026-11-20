using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.DomainEvents;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Decides, translates, and stages the domain events collected during a mediation into the transactional outbox when the mediation completed successfully.
/// </summary>
/// <typeparam name="TMessage">The message type the closed handler observes.</typeparam>
/// <remarks>
///     <para>
///         Registered by <see cref="Abstractions.OutboxModuleBuilder.StageCollectedEvents" /> as an open generic handler,
///         so one registration closes over every mediated message type. It runs at
///         <see cref="HandlerPriorities.Persistence" />, inside the reserved window, which puts it before an
///         application's commit at <see cref="HandlerPriorities.UnitOfWork" />: the staged rows join the transaction the
///         commit applies.
///     </para>
///     <para>
///         Only a <see cref="MediationOutcome.Succeeded" /> mediation stages anything. A failed mediation stages nothing
///         because the staged rows would describe work that never happened, and the collector is scoped to the mediation
///         so its contents are discarded with the scope.
///     </para>
///     <para>
///         Before draining, the handler runs every registered <see cref="IDomainEventHarvester" /> against the
///         collector, so domain events buffered on tracked aggregates move into the collector and stage with the same
///         unit of work. Storage adapters register their harvester through their own builder; without one, the
///         collector drains as before.
///     </para>
///     <para>
///         Each drained event is matched by exact runtime type against the registered per-type decisions: translate
///         through a registered translator, drop without staging, publish in process after the commit, or stage
///         unchanged. Translated messages are published messages and stage directly without a further decision. An
///         event type with no decision follows the configured <see cref="UndecidedDomainEventPolicy" />. Every
///         decision fault throws before the commit, so the business write never commits without its declared
///         consequences.
///     </para>
///     <para>
///         Events that publish in process are recorded for the commit listener when a commit will notify one, which is
///         when the mediation observes a command or a query and a unit of work is registered. Any other mediation has
///         no commit to wait for, so its events publish when staging completes instead. Both paths publish through the
///         event mediator as root mediations with the recorded cause.
///     </para>
///     <para>
///         The transactional outbox is resolved lazily and only when the drain stages at least one message, so
///         mediations that collect nothing never touch it. Binding happens at resolution: under a provider that requires
///         an active transaction, a mediation that collected events without opening one fails here rather than staging
///         rows outside the unit of work.
///     </para>
/// </remarks>
[HandlerPriority(HandlerPriorities.Persistence)]
internal sealed class StageCollectedDomainEventsCompletionHandler<TMessage> : IMessageCompletionHandler<TMessage>
    where TMessage : notnull
{
    /// <summary>
    ///     The mediation-scoped collector drained when the mediation completes successfully.
    /// </summary>
    private readonly IDomainEventCollector _collector;

    /// <summary>
    ///     The enqueue options that decide whether staged envelopes stamp ambient trace and tenant.
    /// </summary>
    private readonly OutboxEnqueueOptions? _enqueueOptions;

    /// <summary>
    ///     The dispatch-scope provider used to resolve the transactional outbox lazily.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StageCollectedDomainEventsCompletionHandler{TMessage}" /> class.
    /// </summary>
    /// <param name="collector">The mediation-scoped collector drained on success.</param>
    /// <param name="serviceProvider">The dispatch-scope provider used to resolve the transactional outbox.</param>
    /// <param name="enqueueOptions">The enqueue options, or <see langword="null" /> when composed without them.</param>
    public StageCollectedDomainEventsCompletionHandler(
        IDomainEventCollector collector,
        IServiceProvider serviceProvider,
        OutboxEnqueueOptions? enqueueOptions = null)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _collector = collector;
        _serviceProvider = serviceProvider;
        _enqueueOptions = enqueueOptions;
    }

    /// <inheritdoc />
    public async Task HandleCompletionAsync(
        MessageCompletionContext<TMessage> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Outcome != MediationOutcome.Succeeded)
        {
            return;
        }

        if (_serviceProvider.GetService(typeof(IEnumerable<IDomainEventHarvester>)) is IEnumerable<IDomainEventHarvester> harvesters)
        {
            foreach (var harvester in harvesters)
            {
                harvester.HarvestInto(_collector);
            }
        }

        var collected = _collector.Drain();

        if (collected.Count == 0)
        {
            return;
        }

        var decisions = _serviceProvider.GetService(typeof(DomainEventDecisions)) as DomainEventDecisions;
        var ambient = AmbientExecutionContext.GetCurrentOrDefault();
        var translationContext = new DomainEventTranslationContext(
            typeof(TMessage),
            _serviceProvider,
            ReadTraceItem(ambient, MessageTraceContextKeys.CorrelationId),
            ReadTraceItem(ambient, MessageTraceContextKeys.CausationId),
            ambient?.Identity.TenantId);
        var traceContext = ReadTraceItem(ambient, MessageTraceContextKeys.TraceContext);

        List<object> toStage = [];
        List<StagedInProcessDomainEvent>? inProcess = null;

        foreach (var domainEvent in collected)
        {
            var eventType = domainEvent.GetType();

            if (decisions is not null && decisions.IsIgnored(eventType))
            {
                continue;
            }

            if (decisions is not null && decisions.IsPublishedInProcess(eventType))
            {
                inProcess ??= [];
                inProcess.Add(CreateStagedEntry(domainEvent, translationContext, traceContext));
                continue;
            }

            if (decisions is not null && decisions.IsStaged(eventType))
            {
                toStage.Add(domainEvent);
                continue;
            }

            if (decisions is not null && decisions.TryGetTranslator(eventType, out var registration))
            {
                var translated = await registration.TranslateAsync(domainEvent, translationContext, cancellationToken).ConfigureAwait(false);

                if (translated is null)
                {
                    throw new MessageFaultException(
                        typeof(TMessage),
                        DomainEventFaultCodes.InvalidTranslation,
                        $"The translator for domain event '{eventType.FullName}' returned a null list. " +
                        "Return an empty list when the event stages nothing.");
                }

                foreach (var message in translated)
                {
                    if (message is null)
                    {
                        throw new MessageFaultException(
                            typeof(TMessage),
                            DomainEventFaultCodes.InvalidTranslation,
                            $"The translator for domain event '{eventType.FullName}' returned a list containing a null message. " +
                            "Translate every domain event into non-null messages.");
                    }

                    toStage.Add(message);
                }

                continue;
            }

            var policy = decisions?.UndecidedPolicy ?? UndecidedDomainEventPolicy.StageAsIs;

            if (policy == UndecidedDomainEventPolicy.Ignore)
            {
                continue;
            }

            if (policy == UndecidedDomainEventPolicy.Throw)
            {
                throw new MessageFaultException(
                    typeof(TMessage),
                    DomainEventFaultCodes.UndecidedDomainEvent,
                    $"Domain event '{eventType.FullName}' has no registered decision, and RequireDomainEventDecision is set. " +
                    "Declare what the event does: translate it, ignore it, publish it in process, or stage it unchanged.");
            }

            toStage.Add(domainEvent);
        }

        if (toStage.Count > 0)
        {
            await StageAsync(toStage, cancellationToken).ConfigureAwait(false);
        }

        if (inProcess is { Count: > 0 })
        {
            await PublishInProcessAsync(inProcess, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Enqueues the staged messages into the transactional outbox.
    /// </summary>
    /// <param name="toStage">The messages to stage, in drain order.</param>
    /// <param name="cancellationToken">The token used to cancel the enqueue.</param>
    /// <returns>A task representing the asynchronous enqueue.</returns>
    private async Task StageAsync(List<object> toStage, CancellationToken cancellationToken)
    {
        var outbox = (ITransactionalOutbox?) _serviceProvider.GetService(typeof(ITransactionalOutbox));

        if (outbox is null)
        {
            throw new InvalidOperationException(
                "StageCollectedEvents drained domain events but no scoped ITransactionalOutbox is registered. " +
                "Register one through the ambient PostgreSQL provider, the Entity Framework Core interceptor " +
                "non-generic ITransactionalOutbox adapter over ITransactionalOutbox<TContext>, the Marten writer, " +
                "or a test double before collecting domain events.");
        }

        var trace = _enqueueOptions?.UseAmbientTrace == true
            ? MessageTrace.FromAmbient()
            : MessageTrace.None.Instance;
        var tenant = _enqueueOptions?.UseAmbientTenant == true
            ? ResolveAmbientTenant()
            : TenantScope.Unscoped.Instance;
        var items = new OutboxEnqueueItem[toStage.Count];

        for (var index = 0; index < toStage.Count; index++)
        {
            var message = toStage[index];

            items[index] = OutboxEnqueueItem.From(
                message,
                message.GetType(),
                OutboxEnqueueMetadata.Immediate with { Trace = trace, Tenant = tenant });
        }

        await outbox.EnqueueBatchAsync(items, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Publishes the recorded domain events in process, after the commit when one will notify the commit listener and when staging completes otherwise.
    /// </summary>
    /// <param name="staged">The recorded domain events, in drain order.</param>
    /// <param name="cancellationToken">The token used to cancel publication.</param>
    /// <returns>A task representing the asynchronous publication.</returns>
    private async Task PublishInProcessAsync(IReadOnlyList<StagedInProcessDomainEvent> staged, CancellationToken cancellationToken)
    {
        var mediator = _serviceProvider.GetService(typeof(IEventMediator)) as IEventMediator;

        if (mediator is null)
        {
            throw new MessageFaultException(
                typeof(TMessage),
                DomainEventFaultCodes.EventMediatorMissing,
                "Recorded domain events cannot publish in process because no IEventMediator is registered. " +
                "Call AddEvents before collecting domain events that publish in process.");
        }

        if (CommitListenerWillRun())
        {
            var buffer = _serviceProvider.GetService(typeof(StagedInProcessDomainEvents)) as StagedInProcessDomainEvents;

            if (buffer is null || !IsPublishListenerRegistered())
            {
                throw new MessageFaultException(
                    typeof(TMessage),
                    DomainEventFaultCodes.InProcessPublishUnavailable,
                    "Recorded domain events cannot publish after the commit because the scoped buffer or the commit " +
                    "listener is not registered. Enable collected domain event staging before collecting domain events " +
                    "that publish in process.");
            }

            foreach (var entry in staged)
            {
                buffer.Add(entry);
            }

            return;
        }

        await DomainEventInProcessPublisher.PublishAsync(mediator, staged, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Tests whether a commit will notify the commit listener after this mediation.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the mediation observes a command or a query and a unit of work is registered;
    ///     otherwise, <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     Only command and query mediations commit through a unit of work completion handler, which is the only stage
    ///     that notifies commit listeners. Event mediations never commit, so they publish when staging completes.
    /// </remarks>
    private bool CommitListenerWillRun()
    {
        if (_serviceProvider.GetService(typeof(IUnitOfWork)) is null)
        {
            return false;
        }

        var messageType = typeof(TMessage);

        return typeof(ICommand).IsAssignableFrom(messageType) || typeof(IQuery).IsAssignableFrom(messageType);
    }

    /// <summary>
    ///     Tests whether the in-process publish commit listener is registered in the commit listener collection.
    /// </summary>
    /// <returns><see langword="true" /> when the listener will drain the scoped buffer; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     Only a listener registered against the commit listener collection drains the buffer, because the unit of
    ///     work completion handler resolves the collection. A listener registered against its own type alone never
    ///     fires, so it does not count.
    /// </remarks>
    private bool IsPublishListenerRegistered()
    {
        if (_serviceProvider.GetService(typeof(IEnumerable<IUnitOfWorkCommitListener>)) is not IEnumerable<IUnitOfWorkCommitListener> listeners)
        {
            return false;
        }

        foreach (var listener in listeners)
        {
            if (listener is DomainEventInProcessPublishListener)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Records one domain event for publication in process with the cause its mediation carries.
    /// </summary>
    /// <param name="domainEvent">The harvested or collected domain event.</param>
    /// <param name="translationContext">The ambient metadata captured for this staging pass.</param>
    /// <param name="traceContext">The distributed trace context of the causing mediation, or <see langword="null" /> when it carries none.</param>
    /// <returns>The recorded domain event.</returns>
    /// <remarks>
    ///     The cause continues the ambient correlation when the mediation carries one and starts a new workflow
    ///     otherwise. Each recorded event gets its own causation identifier, because each domain event is the distinct
    ///     committed fact that caused its mediation.
    /// </remarks>
    private static StagedInProcessDomainEvent CreateStagedEntry(object domainEvent, DomainEventTranslationContext translationContext, string? traceContext)
    {
        var correlationId = string.IsNullOrWhiteSpace(translationContext.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : translationContext.CorrelationId;
        var cause = new MediationCause(correlationId, Guid.NewGuid().ToString("N"), MediationCauseSource.DomainEvent);

        return new StagedInProcessDomainEvent(domainEvent, cause, translationContext.TenantId, traceContext);
    }

    /// <summary>
    ///     Reads one trace metadata item from the ambient execution context.
    /// </summary>
    /// <param name="ambient">The ambient execution context, or <see langword="null" /> when no mediation is ambient.</param>
    /// <param name="key">The trace metadata item key.</param>
    /// <returns>The trace value, or <see langword="null" /> when the context carries none.</returns>
    private static string? ReadTraceItem(IExecutionContext? ambient, string key)
    {
        if (ambient is null)
        {
            return null;
        }

        return ambient.Items.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }

    /// <summary>
    ///     Resolves the ambient execution identity tenant as enqueue tenant metadata.
    /// </summary>
    /// <returns>The ambient tenant, or <see cref="TenantScope.Unscoped" /> when no mediation carries one.</returns>
    private static TenantScope ResolveAmbientTenant()
    {
        var tenantId = AmbientExecutionContext.GetCurrentOrDefault()?.Identity.TenantId;

        return string.IsNullOrWhiteSpace(tenantId)
            ? TenantScope.Unscoped.Instance
            : new TenantScope.Isolated(tenantId);
    }
}
