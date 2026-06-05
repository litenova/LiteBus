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
    IOutboxTerminalStateStore,
    IOutboxRetentionStore,
    IOutboxDiagnosticsStore
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
            var leased = _envelopes.Values
                .Where(envelope => IsAvailable(envelope, request.Now))
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
    public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _envelopes[messageId] = GetRequired(messageId).AsPublished();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(OutboxEnvelopeFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        lock (_sync)
        {
            _envelopes[failure.Id] = GetRequired(failure.Id).AsFailed(failure.Error, failure.VisibleAfter);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MoveToDeadLetterAsync(OutboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);

        lock (_sync)
        {
            ApplyDeadLetter(deadLetter);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MoveToDeadLetterAsync(IReadOnlyList<OutboxEnvelopeDeadLetter> deadLetters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetters);

        if (deadLetters.Count == 0)
        {
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            foreach (var deadLetter in deadLetters)
            {
                ApplyDeadLetter(deadLetter);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkPublishedAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);

        lock (_sync)
        {
            foreach (var messageId in messageIds)
            {
                _envelopes[messageId] = GetRequired(messageId).AsPublished();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(IReadOnlyList<OutboxEnvelopeFailure> failures, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failures);

        lock (_sync)
        {
            foreach (var failure in failures)
            {
                _envelopes[failure.Id] = GetRequired(failure.Id).AsFailed(failure.Error, failure.VisibleAfter);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RequeueDeadLetterAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            RequeueDeadLetterIfNeeded(messageId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RequeueDeadLetterAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken = default)
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
                .Where(envelope => envelope.Status == OutboxStatus.Published && envelope.CreatedAt < olderThan)
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
    ///     Applies dead-letter state to one stored envelope.
    /// </summary>
    /// <param name="deadLetter">The dead-letter details.</param>
    private void ApplyDeadLetter(OutboxEnvelopeDeadLetter deadLetter)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);

        _envelopes[deadLetter.Id] = GetRequired(deadLetter.Id).AsDeadLettered(deadLetter.Reason);
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
    /// <returns><see langword="true" /> when the envelope is eligible for leasing; otherwise, <see langword="false" />.</returns>
    private static bool IsAvailable(OutboxEnvelope envelope, DateTimeOffset now)
    {
        return ((envelope.Status is OutboxStatus.Pending or OutboxStatus.Failed) &&
                (envelope.VisibleAfter is null || envelope.VisibleAfter <= now)) ||
               (envelope.Status == OutboxStatus.Publishing
                && envelope.LeaseExpiresAt is not null
                && envelope.LeaseExpiresAt <= now);
    }
}
