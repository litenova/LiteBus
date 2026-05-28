using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Accepts events into storage for later publication.
/// </summary>
/// <remarks>
///     <para>
///         Use this API when an event must survive process failure and be published after the surrounding state change
///         commits. `IEventPublisher.PublishAsync` invokes in-process handlers immediately; `IOutboxWriter.AddAsync`
///         records an outbox envelope and returns an acceptance receipt.
///     </para>
///     <para>
///         This writer accepts any non-null event object. Prefer <see cref="IIntegrationOutbox" /> for cross-process
///         integration events because its generic constraint makes the external publication boundary explicit.
///     </para>
///     <para>
///         Register each stored event type in `IMessageContractRegistrar` with a stable name and version. Closed generic
///         event types are supported when each closed shape is registered. Open generic contract definitions are rejected.
///     </para>
/// </remarks>
public interface IOutboxWriter
{
    /// <summary>
    ///     Adds an event to the outbox for later publication.
    /// </summary>
    /// <typeparam name="TEvent">The compile-time event type. The runtime type is used for contract lookup.</typeparam>
    /// <param name="event">The event instance to serialize and store.</param>
    /// <param name="options">
    ///     Optional message metadata such as a caller-supplied message id, topic, correlation id, causation id, and tenant
    ///     id. Use <see cref="OutboxOptions.MessageId" /> when the caller already owns a stable event identifier.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>A receipt containing the durable message id, contract name, version, storage time, and trace metadata.</returns>
    Task<OutboxReceipt<TEvent>> AddAsync<TEvent>(
        TEvent @event,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;
}