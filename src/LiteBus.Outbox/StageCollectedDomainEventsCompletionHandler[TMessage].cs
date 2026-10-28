using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Stages the domain events collected during a mediation into the transactional outbox when the mediation completed successfully.
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
///         The transactional outbox is resolved lazily and only when the drain is non-empty, so mediations that collect
///         nothing never touch it. Binding happens at resolution: under a provider that requires an active transaction,
///         a mediation that collected events without opening one fails here rather than staging rows outside the unit of
///         work.
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
    ///     The enqueue options that decide whether staged envelopes stamp ambient trace.
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

        var collected = _collector.Drain();

        if (collected.Count == 0)
        {
            return;
        }

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
        var items = new OutboxEnqueueItem[collected.Count];

        for (var index = 0; index < collected.Count; index++)
        {
            var domainEvent = collected[index];

            items[index] = OutboxEnqueueItem.From(
                domainEvent,
                domainEvent.GetType(),
                OutboxEnqueueMetadata.Immediate with { Trace = trace });
        }

        await outbox.EnqueueBatchAsync(items, cancellationToken).ConfigureAwait(false);
    }
}
