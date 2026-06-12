using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;

namespace LiteBus.Testing;

/// <summary>
///     In-memory outbox store double for unit and integration tests.
/// </summary>
/// <remarks>
///     Implements the writer, lease, state, dead-letter, retention, and diagnostics roles on one process-local instance.
///     Prefer this type over wiring production storage modules when tests only need deterministic outbox behaviour.
/// </remarks>
public sealed class TestOutboxStore :
    IOutboxStore,
    IOutboxProcessingStore,
    IOutboxOperationsStore
{
    /// <summary>
    ///     Gets the underlying in-memory store that owns envelope state.
    /// </summary>
    private readonly InMemoryOutboxStore _inner = new();

    /// <inheritdoc />
    public Task<RequeueResult> RequeueAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        return _inner.RequeueAsync(messageIds, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeletePublishedOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        return _inner.DeletePublishedOlderThanAsync(olderThan, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<OutboxStatus, int>> GetStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetStatusCountsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoreSchemaInfo> GetSchemaInfoAsync(CancellationToken cancellationToken = default)
    {
        return _inner.GetSchemaInfoAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<OutboxMessagePage> QueryAsync(
        OutboxMessageFilter filter,
        OutboxMessagePageRequest pageRequest,
        CancellationToken cancellationToken = default)
    {
        return _inner.QueryAsync(filter, pageRequest, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(OutboxMessageFilter filter, CancellationToken cancellationToken = default)
    {
        return _inner.PurgeAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
        OutboxLeaseRequest request,
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
    public Task<PersistResult> PersistAsync(IReadOnlyList<OutboxEnvelope> envelopes, CancellationToken cancellationToken = default)
    {
        return _inner.PersistAsync(envelopes, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OutboxEnvelope> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        return _inner.AddAsync(envelope, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxEnvelope>> AddBatchAsync(
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken = default)
    {
        return _inner.AddBatchAsync(envelopes, cancellationToken);
    }

    /// <summary>
    ///     Gets the stored envelope for the supplied message identifier.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <returns>The stored envelope.</returns>
    public OutboxEnvelope Get(Guid messageId)
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