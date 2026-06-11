using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Creates inbox envelopes from acceptance items using contract lookup, serialization, and payload protection.
/// </summary>
/// <remarks>
///     Envelope factories centralize acceptance metadata mapping so <see cref="IInbox" />,
///     <see cref="ITransactionalInbox" />, and Entity Framework Core staging share one creation path.
/// </remarks>
public interface IInboxEnvelopeFactory
{
    /// <summary>
    ///     Creates one inbox envelope from a message instance and acceptance metadata.
    /// </summary>
    /// <param name="item">The message payload and per-message acceptance metadata.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>The inbox envelope ready for store persistence or staging.</returns>
    Task<InboxEnvelope> CreateAsync(
        InboxAcceptItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Creates multiple inbox envelopes from acceptance items in one factory pass.
    /// </summary>
    /// <param name="items">The message payloads and per-message acceptance metadata to serialize.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>
    ///     Inbox envelopes in the same order as <paramref name="items" />.
    /// </returns>
    Task<IReadOnlyList<InboxEnvelope>> CreateBatchAsync(
        IReadOnlyList<InboxAcceptItem> items,
        CancellationToken cancellationToken = default);
}