using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.InMemory;

/// <summary>
///     Thread-safe in-memory outbox store for unit tests and local development.
/// </summary>
/// <remarks>
///     <para>
///         The store keeps envelopes in a process-local dictionary and implements the writer, lease, and state roles on
///         one instance. Leasing uses the <see cref="OutboxLeaseRequest.Now" /> value supplied by the processor, so
///         tests can control lease expiry without waiting on real time.
///     </para>
///     <para>
///         Concurrent callers are serialized with a lock. This is sufficient for single-process tests; it does not
///         simulate cross-process database locking.
///     </para>
/// </remarks>
public sealed class InMemoryOutboxStore :
    IOutboxStore,
    IOutboxLeaseStore,
    IOutboxStateWriter,
    IOutboxDeadLetterStore,
    IOutboxRetentionStore,
    IOutboxDiagnosticsStore,
    IOutboxMessageQuery,
    IOutboxPurgeStore
{
    /// <summary>
    ///     The envelopes keyed by message identifier.
    /// </summary>
    private readonly Dictionary<Guid, OutboxEnvelope> _envelopes = [];

    /// <summary>
    ///     The idempotency keys mapped to the accepted message identifier.
    /// </summary>
    private readonly Dictionary<string, Guid> _idempotencyIndex = new(StringComparer.Ordinal);

    /// <summary>
    ///     The lock that serializes mutations and lease scans.
    /// </summary>
    private readonly object _sync = new();

    /// <inheritdoc />
    public Task<OutboxEnvelope> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        lock (_sync)
        {
            if (_envelopes.TryGetValue(envelope.Id, out var existingById))
            {
                return Task.FromResult(existingById);
            }

            if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey) &&
                _idempotencyIndex.TryGetValue(envelope.IdempotencyKey, out var existingId) &&
                _envelopes.TryGetValue(existingId, out var existingByKey))
            {
                return Task.FromResult(existingByKey);
            }

            _envelopes[envelope.Id] = envelope;

            if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
            {
                _idempotencyIndex[envelope.IdempotencyKey] = envelope.Id;
            }

            return Task.FromResult(envelope);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
        OutboxLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_sync)
        {
            var leaseExpiresAt = request.Now.Add(request.LeaseDuration);
            var staleCutoff = request.Now.Add(-request.LeaseDuration);
            var leased = _envelopes.Values
                .Where(envelope => IsAvailable(envelope, request.Now, staleCutoff))
                .OrderBy(envelope => envelope.CreatedAt)
                .Take(request.BatchSize)
                .Select(envelope => envelope.AsLeased(request.LeaseOwner, leaseExpiresAt))
                .ToArray();

            foreach (var envelope in leased)
            {
                _envelopes[envelope.Id] = envelope;
            }

            return Task.FromResult<IReadOnlyList<OutboxEnvelope>>(leased);
        }
    }

    /// <inheritdoc />
    public Task PersistAsync(IReadOnlyList<OutboxEnvelope> envelopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            foreach (var envelope in envelopes)
            {
                _envelopes[envelope.Id] = ClearLeaseWhenTerminal(envelope);

                if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
                {
                    _idempotencyIndex[envelope.IdempotencyKey] = envelope.Id;
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        if (messageIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            foreach (var messageId in messageIds)
            {
                RequeueDeadLetterIfNeeded(messageId);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> DeletePublishedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var toRemove = _envelopes.Values
                .Where(envelope => envelope.Status == OutboxStatus.Published &&
                                   (envelope.PublishedAt ?? envelope.CreatedAt) < olderThan)
                .Select(envelope => envelope.Id)
                .ToArray();

            foreach (var messageId in toRemove)
            {
                RemoveEnvelope(messageId);
            }

            return Task.FromResult(toRemove.Length);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<OutboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var counts = _envelopes.Values
                .GroupBy(envelope => envelope.Status)
                .ToDictionary(group => group.Key, group => group.Count());

            return Task.FromResult<IReadOnlyDictionary<OutboxStatus, int>>(counts);
        }
    }

    /// <inheritdoc />
    public Task<OutboxMessagePage> QueryAsync(
        OutboxMessageFilter filter,
        OutboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(pageRequest);
        ValidatePageSize(pageRequest.PageSize);

        lock (_sync)
        {
            var query = ApplyFilter(_envelopes.Values, filter);
            query = ApplyCursor(query, pageRequest.Cursor);

            var ordered = query
                .OrderBy(envelope => envelope.CreatedAt)
                .ThenBy(envelope => envelope.Id)
                .Take(pageRequest.PageSize + 1)
                .ToList();

            return Task.FromResult(BuildPage(ordered, pageRequest.PageSize));
        }
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(OutboxMessageFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        lock (_sync)
        {
            var toRemove = ApplyFilter(_envelopes.Values, filter)
                .Select(envelope => envelope.Id)
                .ToArray();

            foreach (var messageId in toRemove)
            {
                if (_envelopes.TryGetValue(messageId, out var envelope) &&
                    !string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
                {
                    _idempotencyIndex.Remove(envelope.IdempotencyKey);
                }

                _envelopes.Remove(messageId);
            }

            return Task.FromResult(toRemove.Length);
        }
    }

    /// <summary>
    ///     Gets the stored envelope for the given message identifier.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <returns>The stored envelope.</returns>
    public OutboxEnvelope Get(Guid messageId)
    {
        lock (_sync)
        {
            return GetRequired(messageId);
        }
    }

    /// <summary>
    ///     Gets a snapshot of all stored envelopes.
    /// </summary>
    /// <returns>All envelopes currently held by the store.</returns>
    public IReadOnlyList<OutboxEnvelope> GetAll()
    {
        lock (_sync)
        {
            return _envelopes.Values.ToList();
        }
    }

    /// <summary>
    ///     Gets a snapshot of stored envelopes filtered by status.
    /// </summary>
    /// <param name="status">The status value to match.</param>
    /// <returns>All envelopes with the supplied status.</returns>
    public IReadOnlyList<OutboxEnvelope> GetAll(OutboxStatus status)
    {
        lock (_sync)
        {
            return _envelopes.Values.Where(envelope => envelope.Status == status).ToList();
        }
    }

    /// <summary>
    ///     Gets a snapshot of stored envelopes filtered by contract name.
    /// </summary>
    /// <param name="contractName">The contract name to match.</param>
    /// <returns>All envelopes with the supplied contract name.</returns>
    public IReadOnlyList<OutboxEnvelope> GetAll(string contractName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        lock (_sync)
        {
            return _envelopes.Values.Where(envelope => envelope.ContractName == contractName).ToList();
        }
    }

    /// <summary>
    ///     Gets a snapshot of stored envelopes filtered by status and contract name.
    /// </summary>
    /// <param name="status">The status value to match.</param>
    /// <param name="contractName">The contract name to match.</param>
    /// <returns>All envelopes matching both filters.</returns>
    public IReadOnlyList<OutboxEnvelope> GetAll(OutboxStatus status, string contractName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);

        lock (_sync)
        {
            return _envelopes.Values
                .Where(envelope => envelope.Status == status && envelope.ContractName == contractName)
                .ToList();
        }
    }

    /// <summary>
    ///     Removes every stored envelope so a test can start from an empty store.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _envelopes.Clear();
            _idempotencyIndex.Clear();
        }
    }

    /// <summary>
    ///     Returns the envelope for the given identifier or throws when it is missing.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <returns>The stored envelope.</returns>
    private OutboxEnvelope GetRequired(Guid messageId)
    {
        return _envelopes[messageId];
    }

    /// <summary>
    ///     Requeues one dead-lettered envelope when it is currently in the dead-letter state.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    private void RequeueDeadLetterIfNeeded(Guid messageId)
    {
        var envelope = GetRequired(messageId);

        if (envelope.Status != OutboxStatus.DeadLettered)
        {
            return;
        }

        _envelopes[messageId] = envelope.AsRequeued();
    }

    /// <summary>
    ///     Removes one envelope and its idempotency index entry when present.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    private void RemoveEnvelope(Guid messageId)
    {
        if (_envelopes.TryGetValue(messageId, out var envelope) &&
            !string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
        {
            _idempotencyIndex.Remove(envelope.IdempotencyKey);
        }

        _envelopes.Remove(messageId);
    }

    /// <summary>
    ///     Determines whether an envelope can be leased at the supplied time.
    /// </summary>
    /// <param name="envelope">The candidate envelope.</param>
    /// <param name="now">The current time used for visibility and lease expiry checks.</param>
    /// <param name="staleCutoff">The earliest created timestamp eligible for stale in-flight reclaim.</param>
    /// <returns><see langword="true" /> when the envelope is eligible for leasing; otherwise, <see langword="false" />.</returns>
    private static bool IsAvailable(OutboxEnvelope envelope, DateTimeOffset now, DateTimeOffset staleCutoff)
    {
        return ((envelope.Status is OutboxStatus.Pending or OutboxStatus.Failed) &&
                (envelope.VisibleAfter is null || envelope.VisibleAfter <= now)) ||
               (envelope.Status == OutboxStatus.Publishing
                && envelope.LeaseExpiresAt is not null
                && envelope.LeaseExpiresAt <= now) ||
               (envelope.Status == OutboxStatus.Publishing
                && envelope.LeaseExpiresAt is null
                && envelope.CreatedAt < staleCutoff);
    }

    /// <summary>
    ///     Applies optional outbox message filters to an in-memory sequence.
    /// </summary>
    /// <param name="source">The envelopes to filter.</param>
    /// <param name="filter">The optional predicates applied to stored rows.</param>
    /// <returns>The filtered sequence.</returns>
    private static IEnumerable<OutboxEnvelope> ApplyFilter(
        IEnumerable<OutboxEnvelope> source,
        OutboxMessageFilter filter)
    {
        if (filter.Statuses is { Count: > 0 })
        {
            var statuses = filter.Statuses.ToHashSet();
            source = source.Where(envelope => statuses.Contains(envelope.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.ContractName))
        {
            source = source.Where(envelope => envelope.ContractName == filter.ContractName);
        }

        if (!string.IsNullOrWhiteSpace(filter.Topic))
        {
            source = source.Where(envelope => envelope.Topic == filter.Topic);
        }

        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
        {
            source = source.Where(envelope => envelope.CorrelationId == filter.CorrelationId);
        }

        if (!string.IsNullOrWhiteSpace(filter.CausationId))
        {
            source = source.Where(envelope => envelope.CausationId == filter.CausationId);
        }

        if (!string.IsNullOrWhiteSpace(filter.TenantId))
        {
            source = source.Where(envelope => envelope.TenantId == filter.TenantId);
        }

        if (filter.CreatedAfter is not null)
        {
            source = source.Where(envelope => envelope.CreatedAt >= filter.CreatedAfter);
        }

        if (filter.CreatedBefore is not null)
        {
            source = source.Where(envelope => envelope.CreatedAt <= filter.CreatedBefore);
        }

        return source;
    }

    /// <summary>
    ///     Applies keyset pagination to an in-memory sequence.
    /// </summary>
    /// <param name="source">The envelopes to page.</param>
    /// <param name="cursor">The opaque cursor from a previous page.</param>
    /// <returns>The sequence positioned after the supplied cursor.</returns>
    private static IEnumerable<OutboxEnvelope> ApplyCursor(IEnumerable<OutboxEnvelope> source, string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return source;
        }

        if (!OutboxMessagePageCursor.TryDecode(cursor, out var cursorCreatedAt, out var cursorMessageId))
        {
            throw new ArgumentException("The cursor is invalid.", nameof(cursor));
        }

        return source.Where(envelope =>
            envelope.CreatedAt > cursorCreatedAt ||
            (envelope.CreatedAt == cursorCreatedAt && envelope.Id.CompareTo(cursorMessageId) > 0));
    }

    /// <summary>
    ///     Builds a page result from one over-fetched query batch.
    /// </summary>
    /// <param name="ordered">The ordered envelopes including one optional lookahead row.</param>
    /// <param name="pageSize">The requested page size.</param>
    /// <returns>The page returned to callers.</returns>
    private static OutboxMessagePage BuildPage(IReadOnlyList<OutboxEnvelope> ordered, int pageSize)
    {
        var hasMore = ordered.Count > pageSize;
        var items = hasMore ? ordered.Take(pageSize).ToList() : ordered;
        var nextCursor = hasMore
            ? OutboxMessagePageCursor.Encode(items[^1].CreatedAt, items[^1].Id)
            : null;

        return new OutboxMessagePage(items, hasMore, nextCursor);
    }

    /// <summary>
    ///     Validates that the requested page size is positive.
    /// </summary>
    /// <param name="pageSize">The requested page size.</param>
    private static void ValidatePageSize(int pageSize)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be greater than zero.");
        }
    }

    /// <summary>
    ///     Clears lease metadata on terminal envelopes before they are stored in memory.
    /// </summary>
    /// <param name="envelope">The post-transition envelope supplied by the processor.</param>
    /// <returns>The envelope with lease fields cleared when the status is terminal.</returns>
    private static OutboxEnvelope ClearLeaseWhenTerminal(OutboxEnvelope envelope)
    {
        if (envelope.Status is OutboxStatus.Published or OutboxStatus.Failed or OutboxStatus.DeadLettered)
        {
            return envelope with
            {
                LeaseOwner = null,
                LeaseExpiresAt = null,
                PublishedAt = envelope.Status == OutboxStatus.Published
                    ? envelope.PublishedAt ?? DateTimeOffset.UtcNow
                    : envelope.PublishedAt
            };
        }

        return envelope;
    }
}
