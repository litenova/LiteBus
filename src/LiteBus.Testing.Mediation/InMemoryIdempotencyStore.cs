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
///         <see cref="TimeProvider" /> to drive the takeover deterministically. A granted claim is fenced by its token
///         exactly like the durable stores: a settle carrying another token, or a settle for a key the caller does not
///         hold, is a no-op.
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
    /// <param name="ClaimToken">The token fencing the current claim.</param>
    private sealed record ClaimEntry(
        string? Fingerprint,
        DateTimeOffset ClaimedAt,
        bool IsCompleted,
        string? Payload,
        string ClaimToken);

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
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="claimTimeout" /> is zero or negative.</exception>
    public InMemoryIdempotencyStore(TimeProvider? timeProvider = null, TimeSpan? claimTimeout = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        ClaimTimeout = claimTimeout ?? TimeSpan.FromMinutes(5);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ClaimTimeout, TimeSpan.Zero);
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
                var granted = NewClaimToken();
                _entries[key] = new ClaimEntry(
                    fingerprint,
                    _timeProvider.GetUtcNow(),
                    IsCompleted: false,
                    Payload: null,
                    granted);
                return Task.FromResult(IdempotencyClaim.Granted(granted));
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
                var taken = NewClaimToken();
                _entries[key] = existing with
                {
                    Fingerprint = fingerprint,
                    ClaimedAt = _timeProvider.GetUtcNow(),
                    ClaimToken = taken
                };
                return Task.FromResult(IdempotencyClaim.Granted(taken));
            }

            return Task.FromResult(IdempotencyClaim.InFlight);
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(string key, string claimToken, string? payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

        lock (_entriesLock)
        {
            // Only the holder settles. A completion for a missing key, or one carrying a token the stored claim no
            // longer carries after a takeover, is a no-op: inserting an applied row here would carry no fingerprint
            // and silently disable mismatch checks for later repeats.
            if (_entries.TryGetValue(key, out var existing) &&
                string.Equals(existing.ClaimToken, claimToken, StringComparison.Ordinal))
            {
                _entries[key] = existing with { IsCompleted = true, Payload = payload };
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string key, string claimToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

        lock (_entriesLock)
        {
            // Only an unsettled claim still carrying the token is removed. A completed record is never deleted here,
            // so a release racing a completion cannot turn an applied key back into a claimable one, and a late
            // release from a superseded claimant cannot free the new claimant's live claim.
            if (_entries.TryGetValue(key, out var existing) &&
                !existing.IsCompleted &&
                string.Equals(existing.ClaimToken, claimToken, StringComparison.Ordinal))
            {
                _entries.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Generates the opaque token fencing one granted claim.
    /// </summary>
    /// <returns>A token unique to the grant.</returns>
    private static string NewClaimToken()
    {
        return Guid.NewGuid().ToString("N");
    }
}
