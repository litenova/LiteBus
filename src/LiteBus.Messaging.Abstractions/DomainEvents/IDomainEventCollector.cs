using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Collects domain events during a mediation so a completion handler can stage them into the outbox before the unit of work commits.
/// </summary>
/// <remarks>
///     <para>
///         A command handler records consequences on this collector instead of publishing them inline, and the staging
///         completion handler registered by <c>OutboxModuleBuilder.StageCollectedEvents</c> drains the collector into
///         <c>ITransactionalOutbox</c> when the mediation completed successfully. Draining is destructive: each collected
///         event is returned once, so a retried mediation stages only what was collected after the previous drain.
///     </para>
///     <para>
///         The default implementation is registered scoped, so one collector instance is shared by every handler in a
///         mediation through the dispatch scope. Event mediations may invoke handlers concurrently, so implementations
///         must be thread-safe.
///     </para>
/// </remarks>
public interface IDomainEventCollector
{
    /// <summary>
    ///     Records one domain event for later staging.
    /// </summary>
    /// <param name="domainEvent">The domain event to stage when the mediation completes successfully.</param>
    void Collect(object domainEvent);

    /// <summary>
    ///     Removes and returns every collected domain event in collection order.
    /// </summary>
    /// <returns>The collected events, or an empty list when nothing was collected.</returns>
    IReadOnlyList<object> Drain();
}
