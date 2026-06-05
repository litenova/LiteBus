using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Default implementation of <see cref="IInboxManager" /> that delegates to configured inbox store roles.
/// </summary>
internal sealed class InboxManager : IInboxManager
{
    /// <summary>
    ///     The page size used when replaying dead-lettered messages in bulk.
    /// </summary>
    private const int DeadLetterRequeuePageSize = 200;

    /// <summary>
    ///     The message query role used for browse operations.
    /// </summary>
    private readonly IInboxMessageQuery _messageQuery;

    /// <summary>
    ///     The purge role used to delete rows that match operator filters.
    /// </summary>
    private readonly IInboxPurgeStore _purgeStore;

    /// <summary>
    ///     The dead-letter role used to replay dead-lettered messages.
    /// </summary>
    private readonly IInboxDeadLetterStore _deadLetterStore;

    /// <summary>
    ///     The diagnostics role used to read aggregate status counts.
    /// </summary>
    private readonly IInboxDiagnosticsStore _diagnosticsStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxManager" /> class.
    /// </summary>
    /// <param name="messageQuery">The message query role used for browse operations.</param>
    /// <param name="purgeStore">The purge role used to delete rows that match operator filters.</param>
    /// <param name="deadLetterStore">The dead-letter role used to replay dead-lettered messages.</param>
    /// <param name="diagnosticsStore">The diagnostics role used to read aggregate status counts.</param>
    public InboxManager(
        IInboxMessageQuery messageQuery,
        IInboxPurgeStore purgeStore,
        IInboxDeadLetterStore deadLetterStore,
        IInboxDiagnosticsStore diagnosticsStore)
    {
        _messageQuery = messageQuery ?? throw new ArgumentNullException(nameof(messageQuery));
        _purgeStore = purgeStore ?? throw new ArgumentNullException(nameof(purgeStore));
        _deadLetterStore = deadLetterStore ?? throw new ArgumentNullException(nameof(deadLetterStore));
        _diagnosticsStore = diagnosticsStore ?? throw new ArgumentNullException(nameof(diagnosticsStore));
    }

    /// <inheritdoc />
    public Task<InboxMessagePage> QueryAsync(
        InboxMessageFilter filter,
        InboxMessagePageRequest pageRequest,
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
                InboxMessageFilter.DeadLettered,
                new InboxMessagePageRequest
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
    public Task<int> PurgeAsync(InboxMessageFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return _purgeStore.PurgeAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(
        CancellationToken cancellationToken = default) =>
        _diagnosticsStore.GetStatusCountsAsync(cancellationToken);
}
