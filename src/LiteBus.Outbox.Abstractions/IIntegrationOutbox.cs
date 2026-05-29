using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Accepts integration events into storage for later publication.
/// </summary>
/// <remarks>
///     <para>
///         Use this API for events that leave the current bounded context, process, or database transaction boundary.
///         The <see cref="IIntegrationEvent" /> constraint keeps external publication separate from ordinary in-process
///         domain notifications.
///     </para>
///     <para>
///         The implementation delegates to <see cref="IOutboxWriter" />. It exists to give application code a narrower
///         dependency when only integration events should be written.
///     </para>
/// </remarks>
public interface IIntegrationOutbox
{
    /// <summary>
    ///     Adds an integration event to the outbox for later publication.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type. The runtime type is used for contract lookup.</typeparam>
    /// <param name="event">The integration event instance to serialize and store.</param>
    /// <param name="options">Optional outbox metadata such as message id, topic, and trace fields.</param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>A receipt containing the outbox message id, contract name, version, storage time, and trace metadata.</returns>
    Task<OutboxReceipt<TEvent>> AddAsync<TEvent>(
        TEvent @event,
        OutboxOptions? options = null,
        CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;
}