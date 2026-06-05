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
    ///     The operations store used for browse, replay, purge, and diagnostics.
    /// </summary>
    private readonly IOutboxOperationsStore _operationsStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxManager" /> class.
    /// </summary>
    /// <param name="operationsStore">The operations store used for browse, replay, purge, and diagnostics.</param>
    public OutboxManager(IOutboxOperationsStore operationsStore)
    {
        _operationsStore = operationsStore ?? throw new ArgumentNullException(nameof(operationsStore));
    }

    /// <inheritdoc />
    public Task<OutboxMessagePage> QueryAsync(
        OutboxMessageFilter filter,
        OutboxMessagePageRequest pageRequest,
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
    public Task<int> PurgeAsync(OutboxMessageFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return _operationsStore.PurgeAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<OutboxStatus, int>> GetStatusCountsAsync(
        CancellationToken cancellationToken = default) =>
        _operationsStore.GetStatusCountsAsync(cancellationToken);
}
