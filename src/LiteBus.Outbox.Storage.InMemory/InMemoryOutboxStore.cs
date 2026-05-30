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
public sealed class InMemoryOutboxStore : IOutboxStore, IOutboxLeaseStore, IOutboxStateStore
{
    /// <summary>
    ///     The envelopes keyed by message identifier.
    /// </summary>
    private readonly Dictionary<Guid, OutboxEnvelope> _envelopes = [];

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
            if (_envelopes.TryGetValue(envelope.Id, out var existing))
            {
                return Task.FromResult(existing);
            }

            _envelopes[envelope.Id] = envelope;
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
            var leased = _envelopes.Values
                .Where(envelope => IsAvailable(envelope, request.Now))
                .OrderBy(envelope => envelope.CreatedAt)
                .Take(request.BatchSize)
                .Select(envelope => envelope with
                {
                    Status = OutboxStatus.Publishing,
                    LeaseOwner = request.LeaseOwner,
                    LeaseExpiresAt = request.Now.Add(request.LeaseDuration),
                    AttemptCount = envelope.AttemptCount + 1
                })
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
            var envelope = GetRequired(messageId);
            _envelopes[messageId] = envelope with
            {
                Status = OutboxStatus.Published,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = null
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(OutboxEnvelopeFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        lock (_sync)
        {
            var envelope = GetRequired(failure.Id);
            _envelopes[failure.Id] = envelope with
            {
                Status = OutboxStatus.Failed,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = failure.Error,
                VisibleAfter = failure.VisibleAfter
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MoveToDeadLetterAsync(OutboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);

        lock (_sync)
        {
            var envelope = GetRequired(deadLetter.Id);
            _envelopes[deadLetter.Id] = envelope with
            {
                Status = OutboxStatus.DeadLettered,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = deadLetter.Reason
            };
        }

        return Task.CompletedTask;
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
    ///     Removes every stored envelope so a test can start from an empty store.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _envelopes.Clear();
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
    ///     Determines whether an envelope can be leased at the supplied time.
    /// </summary>
    /// <param name="envelope">The candidate envelope.</param>
    /// <param name="now">The current time used for visibility and lease expiry checks.</param>
    /// <returns><see langword="true" /> when the envelope is eligible for leasing; otherwise, <see langword="false" />.</returns>
    private static bool IsAvailable(OutboxEnvelope envelope, DateTimeOffset now)
    {
        return ((envelope.Status is OutboxStatus.Pending or OutboxStatus.Failed) &&
                (envelope.VisibleAfter is null || envelope.VisibleAfter <= now)) ||
               (envelope.Status == OutboxStatus.Publishing && envelope.LeaseExpiresAt <= now);
    }
}
