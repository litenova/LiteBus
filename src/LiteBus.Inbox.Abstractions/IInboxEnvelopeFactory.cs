using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Creates inbox envelopes from message instances using contract lookup, serialization, and payload protection.
/// </summary>
/// <remarks>
///     Envelope factories centralize acceptance metadata mapping so <see cref="IInbox" />,
///     <see cref="ITransactionalInbox" />, and Entity Framework Core staging share one creation path.
/// </remarks>
public interface IInboxEnvelopeFactory
{
    /// <summary>
    ///     Creates one inbox envelope from a message instance and optional acceptance metadata.
    /// </summary>
    /// <param name="message">The message instance to serialize.</param>
    /// <param name="messageType">The runtime message type used for contract lookup.</param>
    /// <param name="options">Optional acceptance metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>The inbox envelope ready for store persistence or staging.</returns>
    Task<InboxEnvelope> CreateAsync(
        object message,
        Type messageType,
        InboxOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates multiple inbox envelopes from message instances and optional per-message metadata.
    /// </summary>
    /// <param name="messages">The message instances to serialize.</param>
    /// <param name="messageTypes">
    ///     The runtime message types used for contract lookup. Must contain the same number of entries as
    ///     <paramref name="messages" />.
    /// </param>
    /// <param name="options">
    ///     Optional per-message metadata aligned with <paramref name="messages" />. When omitted, default metadata is used
    ///     for every message. When supplied, the list length must match <paramref name="messages" />.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Inbox envelopes in the same order as <paramref name="messages" />.
    /// </returns>
    Task<IReadOnlyList<InboxEnvelope>> CreateBatchAsync(
        IReadOnlyList<object> messages,
        IReadOnlyList<Type> messageTypes,
        IReadOnlyList<InboxOptions?>? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates multiple inbox envelopes from message instances sharing one compile-time type.
    /// </summary>
    /// <typeparam name="T">The shared compile-time message type. Each instance's runtime type is used for contract lookup.</typeparam>
    /// <param name="messages">The message instances to serialize.</param>
    /// <param name="options">
    ///     Optional per-message metadata aligned with <paramref name="messages" />. When omitted, default metadata is used
    ///     for every message.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Inbox envelopes in the same order as <paramref name="messages" />.
    /// </returns>
    Task<IReadOnlyList<InboxEnvelope>> CreateBatchAsync<T>(
        IReadOnlyList<T> messages,
        IReadOnlyList<InboxOptions?>? options = null,
        CancellationToken cancellationToken = default)
        where T : notnull;
}
