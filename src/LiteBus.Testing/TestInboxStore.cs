using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Testing;

/// <summary>
///     In-memory inbox store double for unit and integration tests.
/// </summary>
/// <remarks>
///     Implements the writer, lease, state, dead-letter, retention, and diagnostics roles on one process-local instance.
///     Prefer this type over wiring production storage modules when tests only need deterministic inbox behaviour.
/// </remarks>
public sealed class TestInboxStore :
    IInboxStore,
    IInboxProcessingStore,
    IInboxOperationsStore
{
    /// <summary>
    ///     Gets the underlying in-memory store that owns envelope state.
    /// </summary>
    private readonly InMemoryInboxStore _inner;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TestInboxStore" /> class.
    /// </summary>
    /// <param name="options">Optional store limits applied to the backing in-memory implementation.</param>
    /// <param name="timeProvider">Optional clock used for lease expiry when requests omit an explicit timestamp.</param>
    public TestInboxStore(InMemoryInboxStoreOptions? options = null, TimeProvider? timeProvider = null)
    {
        _inner = new InMemoryInboxStore(options, timeProvider);
    }

    /// <inheritdoc />
    public Task<RequeueResult> RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        return _inner.RequeueAsync(messageIds, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        return _inner.DeleteCompletedOlderThanAsync(olderThan, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<InboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetStatusCountsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetSchemaInfoAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<InboxMessagePage> QueryAsync(
        InboxMessageFilter filter,
        InboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        return _inner.QueryAsync(filter, pageRequest, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(InboxMessageFilter filter, CancellationToken cancellationToken = default)
    {
        return _inner.PurgeAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
        InboxLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        return _inner.LeasePendingAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RenewLeaseAsync(
        LeaseRenewalRequest request,
        CancellationToken cancellationToken = default)
    {
        return _inner.RenewLeaseAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PersistResult> PersistAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken = default)
    {
        return _inner.PersistAsync(envelopes, cancellationToken);
    }

    /// <inheritdoc />
    public Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        return _inner.AddAsync(envelope, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InboxEnvelope>> AddBatchAsync(
        IReadOnlyList<InboxEnvelope> envelopes,
        CancellationToken cancellationToken = default)
    {
        return _inner.AddBatchAsync(envelopes, cancellationToken);
    }

    /// <summary>
    ///     Gets the stored envelope for the supplied message identifier.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <returns>The stored envelope.</returns>
    public InboxEnvelope Get(Guid messageId)
    {
        return _inner.Get(messageId);
    }

    /// <summary>
    ///     Removes every stored envelope so a test can start from an empty store.
    /// </summary>
    public void Clear()
    {
        _inner.Clear();
    }
}