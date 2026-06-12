using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

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
    ///     Gets the loop timing and retention options for cleanup.
    /// </summary>
    private readonly InboxCleanupHostOptions _cleanupHostOptions;

    /// <summary>
    ///     The operations store used for browse, replay, purge, and diagnostics.
    /// </summary>
    private readonly IInboxOperationsStore _operationsStore;

    /// <summary>
    ///     The coordinator that tracks retention cleanup outcomes.
    /// </summary>
    private readonly InboxRetentionCoordinator _retentionCoordinator;

    /// <summary>
    ///     The retention store used to delete completed rows.
    /// </summary>
    private readonly IInboxRetentionStore _retentionStore;

    /// <summary>
    ///     Gets the clock used to calculate retention cutoffs.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxManager" /> class.
    /// </summary>
    /// <param name="operationsStore">The operations store used for browse, replay, purge, and diagnostics.</param>
    /// <param name="retentionStore">The retention store used to delete completed rows.</param>
    /// <param name="retentionCoordinator">The coordinator that tracks retention cleanup outcomes.</param>
    /// <param name="cleanupHostOptions">The loop timing and retention options for cleanup.</param>
    /// <param name="timeProvider">The clock used to calculate retention cutoffs.</param>
    public InboxManager(
        IInboxOperationsStore operationsStore,
        IInboxRetentionStore retentionStore,
        InboxRetentionCoordinator retentionCoordinator,
        InboxCleanupHostOptions cleanupHostOptions,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(operationsStore);
        _operationsStore = operationsStore;
        ArgumentNullException.ThrowIfNull(retentionStore);
        _retentionStore = retentionStore;
        ArgumentNullException.ThrowIfNull(retentionCoordinator);
        _retentionCoordinator = retentionCoordinator;
        ArgumentNullException.ThrowIfNull(cleanupHostOptions);
        _cleanupHostOptions = cleanupHostOptions;
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
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
    public async Task<InboxEnvelope?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var page = await _operationsStore.QueryAsync(
            new InboxMessageFilter { MessageId = messageId },
            new InboxMessagePageRequest { PageSize = 1 },
            cancellationToken).ConfigureAwait(false);

        return page.Items.Count == 0 ? null : page.Items[0];
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

            var result = await _operationsStore.RequeueAsync(
                page.Items.Select(envelope => envelope.Id).ToArray(),
                cancellationToken).ConfigureAwait(false);

            requeuedCount += result.Requeued;

            if (!page.HasMore)
            {
                return requeuedCount;
            }

            cursor = page.NextCursor;
        }
    }

    /// <inheritdoc />
    public Task<RequeueResult> RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        if (messageIds.Count == 0)
        {
            return Task.FromResult(new RequeueResult(0, 0));
        }

        return _operationsStore.RequeueAsync(messageIds, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(
        InboxMessageFilter filter,
        bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (!confirm && filter.IsUnrestricted())
        {
            throw new InboxManagementException(
                "Purge rejected: unrestricted filter requires confirm=true or at least one narrowing predicate.");
        }

        return _operationsStore.PurgeAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(
        CancellationToken cancellationToken = default)
    {
        return _operationsStore.GetStatusCountsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default)
    {
        return _operationsStore.GetSchemaInfoAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<RetentionRunStatus> GetRetentionStatusAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(_retentionCoordinator.GetStatus());
    }

    /// <inheritdoc />
    public async Task<int> RunRetentionPurgeAsync(CancellationToken cancellationToken = default)
    {
        if (_cleanupHostOptions.Retention is null || _cleanupHostOptions.Retention <= TimeSpan.Zero)
        {
            return 0;
        }

        var runAt = _timeProvider.GetUtcNow();

        try
        {
            var cutoff = runAt - _cleanupHostOptions.Retention.Value;

            var deleted = await _retentionStore.DeleteCompletedOlderThanAsync(cutoff, cancellationToken)
                .ConfigureAwait(false);

            _retentionCoordinator.RecordSuccess(deleted, runAt);
            return deleted;
        }

        // Retention purge failures can originate from any backing store fault; record the failure and rethrow for callers.
        catch (Exception exception)
        {
            _retentionCoordinator.RecordFailure(exception.Message, runAt);
            throw;
        }
    }
}