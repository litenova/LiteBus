using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

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
    ///     The retention store used to delete published rows.
    /// </summary>
    private readonly IOutboxRetentionStore _retentionStore;

    /// <summary>
    ///     The coordinator that tracks retention cleanup outcomes.
    /// </summary>
    private readonly OutboxRetentionCoordinator _retentionCoordinator;

    /// <summary>
    ///     Gets the loop timing and retention options for cleanup.
    /// </summary>
    private readonly OutboxCleanupHostOptions _cleanupHostOptions;

    /// <summary>
    ///     Gets the clock used to calculate retention cutoffs.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxManager" /> class.
    /// </summary>
    /// <param name="operationsStore">The operations store used for browse, replay, purge, and diagnostics.</param>
    /// <param name="retentionStore">The retention store used to delete published rows.</param>
    /// <param name="retentionCoordinator">The coordinator that tracks retention cleanup outcomes.</param>
    /// <param name="cleanupHostOptions">The loop timing and retention options for cleanup.</param>
    /// <param name="timeProvider">The clock used to calculate retention cutoffs.</param>
    public OutboxManager(
        IOutboxOperationsStore operationsStore,
        IOutboxRetentionStore retentionStore,
        OutboxRetentionCoordinator retentionCoordinator,
        OutboxCleanupHostOptions cleanupHostOptions,
        TimeProvider timeProvider)
    {
        _operationsStore = operationsStore ?? throw new ArgumentNullException(nameof(operationsStore));
        _retentionStore = retentionStore ?? throw new ArgumentNullException(nameof(retentionStore));
        _retentionCoordinator = retentionCoordinator ?? throw new ArgumentNullException(nameof(retentionCoordinator));
        _cleanupHostOptions = cleanupHostOptions ?? throw new ArgumentNullException(nameof(cleanupHostOptions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
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
    public async Task<OutboxEnvelope?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var page = await _operationsStore.QueryAsync(
            new OutboxMessageFilter { MessageId = messageId },
            new OutboxMessagePageRequest { PageSize = 1 },
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
    public async Task<int> RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        if (messageIds.Count == 0)
        {
            return 0;
        }

        await _operationsStore.RequeueAsync(messageIds, cancellationToken).ConfigureAwait(false);
        return messageIds.Count;
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(
        OutboxMessageFilter filter,
        bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (!confirm && filter.IsUnrestricted())
        {
            throw new OutboxManagementException(
                "Purge rejected: unrestricted filter requires confirm=true or at least one narrowing predicate.");
        }

        return _operationsStore.PurgeAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<OutboxStatus, int>> GetStatusCountsAsync(
        CancellationToken cancellationToken = default) =>
        _operationsStore.GetStatusCountsAsync(cancellationToken);

    /// <inheritdoc />
    public Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default) =>
        _operationsStore.GetSchemaInfoAsync(cancellationToken);

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
            var deleted = await _retentionStore.DeletePublishedOlderThanAsync(cutoff, cancellationToken)
                .ConfigureAwait(false);
            _retentionCoordinator.RecordSuccess(deleted, runAt);
            return deleted;
        }
        catch (Exception exception)
        {
            _retentionCoordinator.RecordFailure(exception.Message, runAt);
            throw;
        }
    }
}
