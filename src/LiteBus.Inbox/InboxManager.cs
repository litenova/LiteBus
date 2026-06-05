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
    ///     The operations store used for browse, replay, purge, and diagnostics.
    /// </summary>
    private readonly IInboxOperationsStore _operationsStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxManager" /> class.
    /// </summary>
    /// <param name="operationsStore">The operations store used for browse, replay, purge, and diagnostics.</param>
    public InboxManager(IInboxOperationsStore operationsStore)
    {
        _operationsStore = operationsStore ?? throw new ArgumentNullException(nameof(operationsStore));
    }

    /// <inheritdoc />
    public Task<InboxMessagePage> QueryAsync(
        InboxMessageFilter filter,
        InboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(pageRequest);

        return _operationsStore.QueryAsync(filter, pageRequest, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> RequeueDeadLettersAsync(CancellationToken cancellationToken = default)
    {
        var requeuedCount = 0;
        string? cursor = null;

        while (true)
        {
            var page = await _operationsStore.QueryAsync(
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

            await _operationsStore.RequeueAsync(
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

        return _operationsStore.PurgeAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(
        CancellationToken cancellationToken = default) =>
        _operationsStore.GetStatusCountsAsync(cancellationToken);
}
