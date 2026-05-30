using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;

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
public sealed class InMemoryInboxStore : IInboxStore, IInboxLeaseStore, IInboxStateStore
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
    ///     The lock that serializes mutations and lease scans.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     The store options applied at construction time.
    /// </summary>
    private readonly InMemoryInboxStoreOptions _options;

    /// <summary>
    ///     The clock used when lease requests omit an explicit timestamp.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryInboxStore" /> class.
    /// </summary>
    /// <param name="options">The store options.</param>
    /// <param name="timeProvider">The clock used for lease expiry when a lease request omits <see cref="InboxLeaseRequest.Now" />.</param>
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

    /// <inheritdoc />
    public Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
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

            if (_options.Capacity > 0 && _envelopes.Count >= _options.Capacity)
            {
                throw new InvalidOperationException(
                    $"The in-memory inbox store reached its capacity of {_options.Capacity} commands.");
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
            var leased = _envelopes.Values
                .Where(envelope => IsAvailable(envelope, now))
                .OrderBy(envelope => envelope.CreatedAt)
                .Take(request.BatchSize)
                .Select(envelope => envelope with
                {
                    Status = InboxStatus.Processing,
                    LeaseOwner = request.LeaseOwner,
                    LeaseExpiresAt = now.Add(leaseDuration),
                    AttemptCount = envelope.AttemptCount + 1
                })
                .ToArray();

            foreach (var envelope in leased)
            {
                _envelopes[envelope.Id] = envelope;
            }

            return Task.FromResult<IReadOnlyList<InboxEnvelope>>(leased);
        }
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var envelope = GetRequired(commandId);
            _envelopes[commandId] = envelope with
            {
                Status = InboxStatus.Completed,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = null
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(InboxEnvelopeFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        lock (_sync)
        {
            var envelope = GetRequired(failure.Id);
            _envelopes[failure.Id] = envelope with
            {
                Status = InboxStatus.Failed,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = failure.Error,
                VisibleAfter = failure.VisibleAfter
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MoveToDeadLetterAsync(InboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);

        lock (_sync)
        {
            var envelope = GetRequired(deadLetter.Id);
            _envelopes[deadLetter.Id] = envelope with
            {
                Status = InboxStatus.DeadLettered,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = deadLetter.Reason
            };
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Gets the stored envelope for the given command identifier.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <returns>The stored envelope.</returns>
    public InboxEnvelope Get(Guid commandId)
    {
        lock (_sync)
        {
            return GetRequired(commandId);
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
    /// <param name="commandId">The command identifier.</param>
    /// <returns>The stored envelope.</returns>
    private InboxEnvelope GetRequired(Guid commandId)
    {
        return _envelopes[commandId];
    }

    /// <summary>
    ///     Determines whether a command can be leased at the supplied timestamp.
    /// </summary>
    /// <param name="envelope">The candidate envelope.</param>
    /// <param name="now">The effective UTC timestamp for the lease pass.</param>
    /// <returns><see langword="true" /> when the command is eligible for leasing; otherwise, <see langword="false" />.</returns>
    private static bool IsAvailable(InboxEnvelope envelope, DateTimeOffset now)
    {
        return ((envelope.Status is InboxStatus.Pending or InboxStatus.Failed) &&
                (envelope.VisibleAfter is null || envelope.VisibleAfter <= now)) ||
               (envelope.Status == InboxStatus.Processing &&
                envelope.LeaseExpiresAt is not null &&
                envelope.LeaseExpiresAt <= now);
    }
}
