using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     Publishes recorded domain events through the event mediator as root mediations carrying their recorded cause.
/// </summary>
/// <remarks>
///     <para>
///         Each event publishes with the <see cref="MediationCause" /> recorded when it was staged, the tenant of the
///         causing mediation restored without its principal, and the recorded trace metadata copied into the mediation
///         items, so reactions observe the same lineage and ambient values whether they run right after staging or
///         after the commit.
///     </para>
///     <para>
///         Events that implement <see cref="IEvent" /> are sent through the non-generic publisher overload. Plain
///         events are published through a closed generic helper cached per event type.
///     </para>
/// </remarks>
internal static class DomainEventInProcessPublisher
{
    /// <summary>
    ///     Caches closed generic publish delegates keyed by event type to avoid repeated reflection overhead.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<IEventMediator, object, EventMediationSettings, CancellationToken, Task>> PublishDelegateCache = new();

    /// <summary>
    ///     The open generic publish helper resolved once at type initialization.
    /// </summary>
    private static readonly MethodInfo PublishTypedAsyncMethod = typeof(DomainEventInProcessPublisher)
                                                                    .GetMethod(nameof(PublishTypedAsync), BindingFlags.NonPublic | BindingFlags.Static) ??
                                                                throw new InvalidOperationException($"Could not resolve {nameof(PublishTypedAsync)}.");

    /// <summary>
    ///     Publishes every recorded domain event in recording order, each as a root mediation with its recorded cause.
    /// </summary>
    /// <param name="mediator">The event mediator used as the publication target.</param>
    /// <param name="staged">The recorded domain events, in recording order.</param>
    /// <param name="cancellationToken">The token used to cancel publication.</param>
    /// <returns>A task representing the asynchronous publication.</returns>
    /// <remarks>
    ///     The ambient execution context is cleared for the whole publication, so every reaction is a root mediation
    ///     whose <see cref="IExecutionContext.Parent" /> stays <see langword="null" /> while its
    ///     <see cref="IExecutionContext.Cause" /> carries the recorded fact. Other ambient state, such as the current
    ///     activity, still flows, so traces stay connected.
    /// </remarks>
    internal static async Task PublishAsync(
        IEventMediator mediator,
        IReadOnlyList<StagedInProcessDomainEvent> staged,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(staged);

        using (RootMediationScope.Begin())
        {
            foreach (var entry in staged)
            {
                var settings = CreateMediationSettings(entry);

                if (entry.DomainEvent is IEvent liteBusEvent)
                {
                    await mediator.PublishAsync(liteBusEvent, settings, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var publish = PublishDelegateCache.GetOrAdd(entry.DomainEvent.GetType(), CreatePublishDelegate);
                await publish(mediator, entry.DomainEvent, settings, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Publishes a plain domain event through the generic <see cref="IEventMediator.PublishAsync{TEvent}" /> overload.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type closed from the recorded runtime type.</typeparam>
    /// <param name="mediator">The event mediator used as the publication target.</param>
    /// <param name="domainEvent">The recorded domain event instance.</param>
    /// <param name="mediationSettings">The mediation settings carrying the recorded cause, tenant, and trace metadata.</param>
    /// <param name="cancellationToken">The token used to cancel publication.</param>
    /// <returns>A task that completes when publication finishes.</returns>
    private static Task PublishTypedAsync<TEvent>(
        IEventMediator mediator,
        object domainEvent,
        EventMediationSettings mediationSettings,
        CancellationToken cancellationToken)
        where TEvent : notnull
    {
        return mediator.PublishAsync((TEvent) domainEvent, mediationSettings, cancellationToken);
    }

    /// <summary>
    ///     Creates a closed generic publish delegate for the supplied event type.
    /// </summary>
    /// <param name="eventType">The runtime type of the recorded domain event.</param>
    /// <returns>A delegate that publishes through <see cref="IEventMediator.PublishAsync{TEvent}" />.</returns>
    /// <remarks>
    ///     The delegate is built with <c>CreateDelegate</c> rather than <c>MethodInfo.Invoke</c> so reaction failures
    ///     surface unwrapped.
    /// </remarks>
    private static Func<IEventMediator, object, EventMediationSettings, CancellationToken, Task> CreatePublishDelegate(Type eventType)
    {
        var closedMethod = PublishTypedAsyncMethod.MakeGenericMethod(eventType);

        return closedMethod.CreateDelegate<Func<IEventMediator, object, EventMediationSettings, CancellationToken, Task>>();
    }

    /// <summary>
    ///     Creates event mediation settings with the recorded cause, tenant, and trace metadata.
    /// </summary>
    /// <param name="staged">The recorded domain event.</param>
    /// <returns>Event mediation settings configured for in-process publication.</returns>
    /// <remarks>
    ///     The mediation runs in the tenant recorded on the entry, restored through
    ///     <see cref="ExecutionIdentity.ForTenant" /> without the calling principal, which never crosses the
    ///     asynchronous boundary into a reaction.
    /// </remarks>
    private static EventMediationSettings CreateMediationSettings(StagedInProcessDomainEvent staged)
    {
        var settings = new EventMediationSettings
        {
            Cause = staged.Cause,
            Identity = ExecutionIdentity.ForTenant(staged.TenantId)
        };

        MessageProcessorDiagnostics.ApplyTraceMetadata(
            settings.Items,
            staged.Cause.CorrelationId,
            staged.Cause.CausationId,
            staged.TenantId,
            staged.TraceContext);

        return settings;
    }
}
