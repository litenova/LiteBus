using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Enqueues events for persistence in the same Entity Framework Core transaction as domain state.
/// </summary>
/// <remarks>
///     Register Entity Framework Core outbox storage with <c>EnableSaveChangesInterceptor()</c> to resolve
///     <see cref="ITransactionalOutbox" />. Implementations stage envelopes through the EF Core save-changes interceptor so
///     contract resolution and serialization follow the same path as <see cref="IOutbox" />. Use
///     <see cref="ITransactionalOutboxStore" /> when callers already build <see cref="OutboxEnvelope" /> instances and need a
///     context-bound store writer.
/// </remarks>
public interface ITransactionalOutbox
{
    /// <summary>
    ///     Enqueues an event for persistence when the supplied <c>DbContext</c> saves changes.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type. The runtime type is used for contract lookup.</typeparam>
    /// <param name="dbContext">
    ///     The active Entity Framework Core context that will invoke <c>SaveChanges</c>. Must be the same instance
    ///     registered with the save-changes interceptor.
    /// </param>
    /// <param name="event">The event instance to serialize and stage.</param>
    /// <param name="options">Optional enqueue metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>A receipt describing the staged outbox message.</returns>
    Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(
        object dbContext,
        TEvent @event,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
