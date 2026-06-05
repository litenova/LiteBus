using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Default implementation of <see cref="IOutboxManager" /> that delegates to configured outbox store roles.
/// </summary>
internal sealed class OutboxManager : IOutboxManager
{
    /// <summary>
    ///     The page size used when replaying dead-lettered messages in bulk.
    /// </summary>
    private const int DeadLetterRequeuePageSize = 200;

    /// <summary>
    ///     The message query role used for browse operations.
    /// </summary>
    private readonly IOutboxMessageQuery _messageQuery;

    /// <summary>
    ///     The purge role used to delete rows that match operator filters.
    /// </summary>
    private readonly IOutboxPurgeStore _purgeStore;

    /// <summary>
    ///     The dead-letter role used to replay dead-lettered messages.
    /// </summary>
    private readonly IOutboxDeadLetterStore _deadLetterStore;

    /// <summary>
    ///     The diagnostics role used to read aggregate status counts.
    /// </summary>
    private readonly IOutboxDiagnosticsStore _diagnosticsStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxManager" /> class.
    /// </summary>
    /// <param name="messageQuery">The message query role used for browse operations.</param>
    /// <param name="purgeStore">The purge role used to delete rows that match operator filters.</param>
    /// <param name="deadLetterStore">The dead-letter role used to replay dead-lettered messages.</param>
    /// <param name="diagnosticsStore">The diagnostics role used to read aggregate status counts.</param>
    public OutboxManager(
        IOutboxMessageQuery messageQuery,
        IOutboxPurgeStore purgeStore,
        IOutboxDeadLetterStore deadLetterStore,
        IOutboxDiagnosticsStore diagnosticsStore)
    {
        _messageQuery = messageQuery ?? throw new ArgumentNullException(nameof(messageQuery));
        _purgeStore = purgeStore ?? throw new ArgumentNullException(nameof(purgeStore));
        _deadLetterStore = deadLetterStore ?? throw new ArgumentNullException(nameof(deadLetterStore));
        _diagnosticsStore = diagnosticsStore ?? throw new ArgumentNullException(nameof(diagnosticsStore));
    }

    /// <inheritdoc />
    public Task<OutboxMessagePage> QueryAsync(
        OutboxMessageFilter filter,
        OutboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(pageRequest);

        return _messageQuery.QueryAsync(filter, pageRequest, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> RequeueDeadLettersAsync(CancellationToken cancellationToken = default)
    {
        var requeuedCount = 0;
        string? cursor = null;

        while (true)
        {
            var page = await _messageQuery.QueryAsync(
                OutboxMessageFilter.DeadLettered,
                new OutboxMessagePageRequest
                {
                    PageSize = DeadLetterRequeuePageSize,
                    Cursor = cursor
                },
                cancellationToken).ConfigureAwait(false);

            if (page.Items.Count == 0)
            {
                return requeuedCount;
            }

            await _deadLetterStore.RequeueAsync(
                page.Items.Select(envelope => envelope.Id).ToArray(),
                cancellationToken).ConfigureAwait(false);

            requeuedCount += page.Items.Count;

            if (!page.HasMore)
            {
                return requeuedCount;
            }

            cursor = page.NextCursor;
        }
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(OutboxMessageFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return _purgeStore.PurgeAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<OutboxStatus, int>> GetStatusCountsAsync(
        CancellationToken cancellationToken = default) =>
        _diagnosticsStore.GetStatusCountsAsync(cancellationToken);
}
