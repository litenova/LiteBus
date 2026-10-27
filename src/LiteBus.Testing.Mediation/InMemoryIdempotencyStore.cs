using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Testing.Mediation;

/// <summary>
///     An <see cref="IIdempotencyStore" /> that remembers keys in process memory.
/// </summary>
/// <remarks>
///     <para>
///         A test double, and only a test double. It is shipped from the testing package rather than the runtime one so
///         that reaching for it in production takes a package reference nobody adds by accident.
///     </para>
///     <para>
///         Two reasons it cannot be used for real. It forgets everything when the process restarts, so a redelivery
///         after a deploy is applied twice. And it is per-process, so a second instance behind a load balancer shares
///         nothing and both apply the same message. Idempotency is a claim about the system, and a store that only
///         knows about one process cannot make it.
///     </para>
///     <para>
///         It is also not transactional. A real store claims the key through the same unit of work that applies the
///         change, so a crash between the two cannot leave a key claimed for work that never happened.
///     </para>
///     <para>
///         It does answer like a durable store: a key held by an unsettled claim reports
///         <see cref="IdempotencyClaimOutcome.InFlight" /> until the claim is older than <see cref="ClaimTimeout" />,
///         when the new caller takes it over, and an applied key whose stored fingerprint differs from the incoming one
///         reports <see cref="IdempotencyClaimOutcome.FingerprintMismatch" />. Pass a controlled
///         <see cref="TimeProvider" /> to drive the takeover deterministically.
///     </para>
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    /// <summary>
    ///     One key's remembered state: the fingerprint it was claimed with, when it was claimed, and what settled it.
    /// </summary>
    /// <param name="Fingerprint">The fingerprint the claiming delivery sent, or <see langword="null" /> when none.</param>
    /// <param name="ClaimedAt">When the current claim was taken.</param>
    /// <param name="IsCompleted">Whether the key was marked applied.</param>
    /// <param name="Payload">The serialized result recorded for replay, or <see langword="null" /> when none.</param>
    private sealed record ClaimEntry(string? Fingerprint, DateTimeOffset ClaimedAt, bool IsCompleted, string? Payload);

    /// <summary>
    ///     Guards <see cref="_entries" />, so a claim is checked and recorded atomically.
    /// </summary>
    private readonly object _entriesLock = new();

    /// <summary>
    ///     The remembered keys and their state.
    /// </summary>
    private readonly Dictionary<string, ClaimEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    ///     Supplies the clock claims are aged against.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryIdempotencyStore" /> class.
    /// </summary>
    /// <param name="timeProvider">Supplies the clock claims are aged against. Defaults to the system clock.</param>
    /// <param name="claimTimeout">
    ///     How long an unsettled claim blocks its key before a new caller takes it over. Defaults to five minutes.
    /// </param>
    public InMemoryIdempotencyStore(TimeProvider? timeProvider = null, TimeSpan? claimTimeout = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        ClaimTimeout = claimTimeout ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    ///     Gets how long an unsettled claim blocks its key before a new caller takes it over.
    /// </summary>
    public TimeSpan ClaimTimeout { get; }

    /// <summary>
    ///     Gets the keys recorded as applied.
    /// </summary>
    public IReadOnlyCollection<string> AppliedKeys
    {
        get
        {
            lock (_entriesLock)
            {
                return _entries
                    .Where(entry => entry.Value.IsCompleted)
                    .Select(entry => entry.Key)
                    .ToList();
            }
        }
    }

    /// <inheritdoc />
    public Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(key, out var existing))
            {
                _entries[key] = new ClaimEntry(fingerprint, _timeProvider.GetUtcNow(), IsCompleted: false, Payload: null);
                return Task.FromResult(IdempotencyClaim.Granted);
            }

            if (existing.IsCompleted)
            {
                if (existing.Fingerprint is not null
                    && fingerprint is not null
                    && !string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return Task.FromResult(IdempotencyClaim.FingerprintMismatch);
                }

                return Task.FromResult(IdempotencyClaim.AlreadyCompleted(existing.Payload));
            }

            if (_timeProvider.GetUtcNow() - existing.ClaimedAt > ClaimTimeout)
            {
                _entries[key] = existing with { Fingerprint = fingerprint, ClaimedAt = _timeProvider.GetUtcNow() };
                return Task.FromResult(IdempotencyClaim.Granted);
            }

            return Task.FromResult(IdempotencyClaim.InFlight);
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(string key, string? payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_entriesLock)
        {
            _entries.TryGetValue(key, out var existing);
            _entries[key] = new ClaimEntry(
                existing?.Fingerprint,
                existing?.ClaimedAt ?? _timeProvider.GetUtcNow(),
                IsCompleted: true,
                payload);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_entriesLock)
        {
            // Only an unsettled claim is removed. A completed record is never deleted here, so a release racing a
            // completion cannot turn an applied key back into a claimable one.
            if (_entries.TryGetValue(key, out var existing) && !existing.IsCompleted)
            {
                _entries.Remove(key);
            }
        }

        return Task.CompletedTask;
    }
}
