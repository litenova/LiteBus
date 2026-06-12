using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using static LiteBus.Messaging.Abstractions.DurableMessaging.DurableIdempotencyScope;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Inbox.Storage.InMemory;

/// <summary>
///     Thread-safe in-memory inbox store for unit tests and local development.
/// </summary>
/// <remarks>
///     <para>
///         The store keeps envelopes in a process-local dictionary and implements the writer, lease, and state roles on
///         one instance. Leasing uses the <see cref="InboxLeaseRequest.Now" /> value supplied by the processor when it is
///         set; otherwise the injected <see cref="TimeProvider" /> supplies the lease clock.
///     </para>
///     <para>
///         Concurrent callers are serialized with a lock. This is sufficient for single-process tests; it does not
///         simulate cross-process database locking.
///     </para>
/// </remarks>
public sealed class InMemoryInboxStore :
    IInboxStore,
    IInboxProcessingStore,
    IInboxOperationsStore
{
    /// <summary>
    ///     The envelopes keyed by command identifier.
    /// </summary>
    private readonly Dictionary<Guid, InboxEnvelope> _envelopes = [];

    /// <summary>
    ///     The idempotency keys mapped to the accepted command identifier.
    /// </summary>
    private readonly Dictionary<string, Guid> _idempotencyIndex = new(StringComparer.Ordinal);

    /// <summary>
    ///     The store options applied at construction time.
    /// </summary>
    private readonly InMemoryInboxStoreOptions _options;

    /// <summary>
    ///     The lock that serializes mutations and lease scans.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     The clock used when lease requests omit an explicit timestamp.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryInboxStore" /> class.
    /// </summary>
    /// <param name="options">The store options.</param>
    /// <param name="timeProvider">
    ///     The clock used for lease expiry when a lease request omits
    ///     <see cref="InboxLeaseRequest.Now" />.
    /// </param>
    public InMemoryInboxStore(InMemoryInboxStoreOptions? options = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? new InMemoryInboxStoreOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_options.Capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), _options.Capacity, "Capacity cannot be negative.");
        }

        if (_options.DefaultLeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _options.DefaultLeaseDuration,
                "Default lease duration must be greater than zero.");
        }
    }

    /// <summary>
    ///     Gets the number of commands currently stored.
    /// </summary>
    /// <returns>The stored command count.</returns>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _envelopes.Count;
            }
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

        var requeued = 0;

        lock (_sync)
        {
            foreach (var messageId in messageIds)
            {
                if (RequeueDeadLetterIfNeeded(messageId))
                {
                    requeued++;
                }
            }
        }

        return Task.FromResult(new RequeueResult(messageIds.Count, requeued));
    }

    /// <inheritdoc />
    public Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var toRemove = _envelopes.Values
                .Where(envelope => envelope.Status == InboxStatus.Completed &&
                                   (envelope.CompletedAt ?? envelope.CreatedAt) < olderThan)
                .Select(envelope => envelope.Id)
                .ToArray();

            foreach (var messageId in toRemove)
            {
                if (_envelopes.TryGetValue(messageId, out var envelope) &&
                    !string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
                {
                    _idempotencyIndex.Remove(CreateScopeKey(envelope.TenantId, envelope.IdempotencyKey));
                }

                _envelopes.Remove(messageId);
            }

            return Task.FromResult(toRemove.Length);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var counts = _envelopes.Values
                .GroupBy(envelope => envelope.Status)
                .ToDictionary(group => group.Key, group => group.Count());

            return Task.FromResult<IReadOnlyDictionary<InboxStatus, int>>(counts);
        }
    }

    /// <inheritdoc />
    public Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(StoreSchemaInfo.ForLogicalStore("inbox", 1));
    }

    /// <inheritdoc />
    public Task<InboxMessagePage> QueryAsync(
        InboxMessageFilter filter,
        InboxMessagePageRequest pageRequest,
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
    public Task<int> PurgeAsync(InboxMessageFilter filter, CancellationToken cancellationToken = default)
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
                    _idempotencyIndex.Remove(CreateScopeKey(envelope.TenantId, envelope.IdempotencyKey));
                }

                _envelopes.Remove(messageId);
            }

            return Task.FromResult(toRemove.Length);
        }
    }

    /// <inheritdoc />
    public Task<bool> RenewLeaseAsync(
        LeaseRenewalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LeaseOwner);

        lock (_sync)
        {
            if (!_envelopes.TryGetValue(request.MessageId, out var envelope) ||
                envelope.Status != InboxStatus.Processing ||
                !string.Equals(envelope.LeaseOwner, request.LeaseOwner, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _envelopes[request.MessageId] = envelope with { LeaseExpiresAt = request.ExpiresAt };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
        InboxLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = ResolveLeaseClock(request);

        var leaseDuration = request.LeaseDuration > TimeSpan.Zero
            ? request.LeaseDuration
            : _options.DefaultLeaseDuration;

        lock (_sync)
        {
            var leaseExpiresAt = now.Add(leaseDuration);
            var staleCutoff = now.Add(-leaseDuration);

            var leased = _envelopes.Values
                .Where(envelope => MatchesTenantFilter(request.TenantId, envelope.TenantId))
                .Where(envelope => IsAvailable(envelope, now, staleCutoff))
                .OrderBy(envelope => envelope.CreatedAt)
                .Take(request.BatchSize)
                .Select(envelope => envelope.AsLeased(request.LeaseOwner, leaseExpiresAt))
                .ToArray();

            foreach (var envelope in leased)
            {
                _envelopes[envelope.Id] = envelope;
            }

            return Task.FromResult<IReadOnlyList<InboxEnvelope>>(leased);
        }
    }

    /// <inheritdoc />
    public Task<PersistResult> PersistAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return Task.FromResult(PersistResult.Empty);
        }

        HashSet<Guid> persistedMessageIds;

        lock (_sync)
        {
            persistedMessageIds = [];

            foreach (var envelope in envelopes)
            {
                if (!TryPersistTerminal(envelope))
                {
                    continue;
                }

                persistedMessageIds.Add(envelope.Id);

                if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
                {
                    _idempotencyIndex[CreateScopeKey(envelope.TenantId, envelope.IdempotencyKey)] = envelope.Id;
                }
            }
        }

        var messageIds = envelopes.Select(envelope => envelope.Id).ToArray();
        return Task.FromResult(PersistResult.FromMessageIds(messageIds, persistedMessageIds));
    }

    /// <inheritdoc />
    public Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        lock (_sync)
        {
            return Task.FromResult(AddCore(envelope));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InboxEnvelope>> AddBatchAsync(
        IReadOnlyList<InboxEnvelope> envelopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        if (envelopes.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<InboxEnvelope>>([]);
        }

        lock (_sync)
        {
            var stored = new InboxEnvelope[envelopes.Count];

            for (var index = 0; index < envelopes.Count; index++)
            {
                stored[index] = AddCore(envelopes[index]);
            }

            return Task.FromResult<IReadOnlyList<InboxEnvelope>>(stored);
        }
    }

    /// <summary>
    ///     Gets the stored envelope for the given message identifier.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <returns>The stored envelope.</returns>
    public InboxEnvelope Get(Guid messageId)
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
    public IReadOnlyList<InboxEnvelope> GetAll()
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
    public IReadOnlyList<InboxEnvelope> GetAll(InboxStatus status)
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
    public IReadOnlyList<InboxEnvelope> GetAll(string contractName)
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
    public IReadOnlyList<InboxEnvelope> GetAll(InboxStatus status, string contractName)
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
    ///     Removes every stored command so a test can start from an empty store.
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
    ///     Resolves the clock used for lease visibility and expiry checks.
    /// </summary>
    /// <param name="request">The lease request.</param>
    /// <returns>The effective UTC timestamp for the lease pass.</returns>
    private DateTimeOffset ResolveLeaseClock(InboxLeaseRequest request)
    {
        return request.Now == default ? _timeProvider.GetUtcNow() : request.Now;
    }

    /// <summary>
    ///     Returns the envelope for the given identifier or throws when it is missing.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <returns>The stored envelope.</returns>
    private InboxEnvelope GetRequired(Guid messageId)
    {
        return _envelopes[messageId];
    }

    /// <summary>
    ///     Requeues one dead-lettered envelope when it is currently in the dead-letter state.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <returns><see langword="true" /> when the row was requeued; otherwise <see langword="false" />.</returns>
    private bool RequeueDeadLetterIfNeeded(Guid messageId)
    {
        if (!_envelopes.TryGetValue(messageId, out var envelope) || envelope.Status != InboxStatus.DeadLettered)
        {
            return false;
        }

        _envelopes[messageId] = envelope.AsRequeued();
        return true;
    }

    /// <summary>
    ///     Determines whether one stored row matches the tenant scope on a lease request.
    /// </summary>
    /// <param name="requestedTenantId">The tenant filter from the lease request, if any.</param>
    /// <param name="storedTenantId">The tenant stored on the candidate row.</param>
    /// <returns><see langword="true" /> when the row is visible to the lease request.</returns>
    private static bool MatchesTenantFilter(string? requestedTenantId, string? storedTenantId)
    {
        return requestedTenantId is null || string.Equals(storedTenantId, requestedTenantId, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Determines whether a command can be leased at the supplied timestamp.
    /// </summary>
    /// <param name="envelope">The candidate envelope.</param>
    /// <param name="now">The effective UTC timestamp for the lease pass.</param>
    /// <param name="staleCutoff">The earliest created timestamp eligible for stale in-flight reclaim.</param>
    /// <returns><see langword="true" /> when the command is eligible for leasing; otherwise, <see langword="false" />.</returns>
    private static bool IsAvailable(InboxEnvelope envelope, DateTimeOffset now, DateTimeOffset staleCutoff)
    {
        return envelope.Status is InboxStatus.Pending or InboxStatus.Failed &&
               (envelope.VisibleAfter is null || envelope.VisibleAfter <= now) ||
               envelope is { Status: InboxStatus.Processing, LeaseExpiresAt: not null } &&
               envelope.LeaseExpiresAt <= now ||
               envelope is { Status: InboxStatus.Processing, LeaseExpiresAt: null } &&
               envelope.CreatedAt < staleCutoff;
    }

    /// <summary>
    ///     Applies optional inbox message filters to an in-memory sequence.
    /// </summary>
    /// <param name="source">The envelopes to filter.</param>
    /// <param name="filter">The optional predicates applied to stored rows.</param>
    /// <returns>The filtered sequence.</returns>
    private static IEnumerable<InboxEnvelope> ApplyFilter(
        IEnumerable<InboxEnvelope> source,
        InboxMessageFilter filter)
    {
        if (filter.MessageId is not null)
        {
            source = source.Where(envelope => envelope.Id == filter.MessageId);
        }

        if (filter.MessageIds is { Count: > 0 })
        {
            var messageIds = filter.MessageIds.ToHashSet();
            source = source.Where(envelope => messageIds.Contains(envelope.Id));
        }

        if (filter.Statuses is { Count: > 0 })
        {
            var statuses = filter.Statuses.ToHashSet();
            source = source.Where(envelope => statuses.Contains(envelope.Status));
        }

        if (!string.IsNullOrWhiteSpace(filter.ContractName))
        {
            source = source.Where(envelope => envelope.ContractName == filter.ContractName);
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
    private static IEnumerable<InboxEnvelope> ApplyCursor(IEnumerable<InboxEnvelope> source, string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return source;
        }

        if (!InboxMessagePageCursor.TryDecode(cursor, out var cursorCreatedAt, out var cursorMessageId))
        {
            throw new ArgumentException("The cursor is invalid.", nameof(cursor));
        }

        return source.Where(envelope =>
            envelope.CreatedAt > cursorCreatedAt ||
            envelope.CreatedAt == cursorCreatedAt && envelope.Id.CompareTo(cursorMessageId) > 0);
    }

    /// <summary>
    ///     Builds a page result from one over-fetched query batch.
    /// </summary>
    /// <param name="ordered">The ordered envelopes including one optional lookahead row.</param>
    /// <param name="pageSize">The requested page size.</param>
    /// <returns>The page returned to callers.</returns>
    private static InboxMessagePage BuildPage(IReadOnlyList<InboxEnvelope> ordered, int pageSize)
    {
        var hasMore = ordered.Count > pageSize;
        var items = hasMore ? ordered.Take(pageSize).ToList() : ordered;

        var nextCursor = hasMore
            ? InboxMessagePageCursor.Encode(items[^1].CreatedAt, items[^1].Id)
            : null;

        return new InboxMessagePage(items, hasMore, nextCursor);
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
    ///     Inserts one envelope or returns the existing row for duplicate identifiers or idempotency keys.
    /// </summary>
    /// <param name="envelope">The envelope to store.</param>
    /// <returns>The stored envelope.</returns>
    private InboxEnvelope AddCore(InboxEnvelope envelope)
    {
        if (_envelopes.TryGetValue(envelope.Id, out var existingById))
        {
            ThrowIfStrictConflict(envelope, existingById.Id);
            return existingById;
        }

        if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey) &&
            _idempotencyIndex.TryGetValue(CreateScopeKey(envelope.TenantId, envelope.IdempotencyKey), out var existingId) &&
            _envelopes.TryGetValue(existingId, out var existingByKey))
        {
            ThrowIfStrictConflict(envelope, existingByKey.Id);
            return existingByKey;
        }

        if (_options.Capacity > 0 && _envelopes.Count >= _options.Capacity)
        {
            throw new InboxStorageException(
                $"The in-memory inbox store reached its capacity of {_options.Capacity} commands.");
        }

        _envelopes[envelope.Id] = envelope;

        if (!string.IsNullOrWhiteSpace(envelope.IdempotencyKey))
        {
            _idempotencyIndex[CreateScopeKey(envelope.TenantId, envelope.IdempotencyKey)] = envelope.Id;
        }

        return envelope;
    }

    /// <summary>
    ///     Persists one terminal envelope when the stored row is still leased by the same owner.
    /// </summary>
    /// <param name="envelope">The post-transition envelope supplied by the processor.</param>
    /// <returns><see langword="true" /> when the row was updated; otherwise <see langword="false" />.</returns>
    private bool TryPersistTerminal(InboxEnvelope envelope)
    {
        if (!_envelopes.TryGetValue(envelope.Id, out var existing))
        {
            return false;
        }

        if (envelope.Status is InboxStatus.Completed or InboxStatus.Failed or InboxStatus.DeadLettered)
        {
            if (existing.Status != InboxStatus.Processing ||
                !string.Equals(existing.LeaseOwner, envelope.LeaseOwner, StringComparison.Ordinal))
            {
                return false;
            }
        }

        _envelopes[envelope.Id] = ClearLeaseWhenTerminal(envelope);
        return true;
    }

    /// <summary>
    ///     Clears lease metadata on terminal envelopes before they are stored in memory.
    /// </summary>
    /// <param name="envelope">The post-transition envelope supplied by the processor.</param>
    /// <returns>The envelope with lease fields cleared when the status is terminal.</returns>
    private static InboxEnvelope ClearLeaseWhenTerminal(InboxEnvelope envelope)
    {
        if (envelope.Status is InboxStatus.Completed or InboxStatus.Failed or InboxStatus.DeadLettered)
        {
            return envelope with
            {
                LeaseOwner = null,
                LeaseExpiresAt = null,
                CompletedAt = envelope.Status == InboxStatus.Completed
                    ? envelope.CompletedAt ?? DateTimeOffset.UtcNow
                    : envelope.CompletedAt
            };
        }

        return envelope;
    }

    /// <summary>
    ///     Throws when a duplicate idempotency key or message identifier is rejected under strict conflict mode.
    /// </summary>
    /// <param name="envelope">The envelope attempted for insert.</param>
    /// <param name="existingId">The identifier of the stored row that conflicts with the attempt.</param>
    private static void ThrowIfStrictConflict(InboxEnvelope envelope, Guid existingId)
    {
        if (envelope.IdempotencyConflictMode != IdempotencyConflictMode.Strict)
        {
            return;
        }

        if (envelope.Id == existingId)
        {
            return;
        }

        throw new IdempotencyConflictException(
            $"An inbox message with idempotency key '{envelope.IdempotencyKey}' or message id '{envelope.Id}' already exists.");
    }
}