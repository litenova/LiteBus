using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using Marten;
using Marten.Exceptions;
using Npgsql;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Marten-backed idempotency store with serializable claim sessions on one document per key.
/// </summary>
/// <remarks>
///     <para>
///         Every call opens its own short serializable session from <see cref="IDocumentStore" /> and commits it
///         before returning, so a granted claim is visible to a concurrent claimant immediately. The claim therefore
///         cannot wait for the handler session commit, and it is not atomic with the writes the handler applies: a
///         crash between the claim commit and the handler commit leaves the key claimed for work that never happened.
///         Only a mediation that ends inside the process settles its claim, through the completion handler the
///         idempotency feature registers.
///     </para>
///     <para>
///         Claim atomicity comes from two properties working together. The document identity is the key, so two
///         concurrent claims contend on one row. The session is serializable, so the storage engine refuses the loser:
///         a new-key race fails with a unique violation (<c>23505</c>), a takeover race fails with a serialization
///         failure (<c>40001</c>), and a deadlock between two claimants fails with <c>40P01</c>. Each is retried
///         against the winner, so two concurrent claims never both grant. Any other failure propagates to the caller.
///     </para>
///     <para>
///         An unsettled claim older than <see cref="MartenIdempotencyStoreOptions.ClaimTimeout" /> may be taken over:
///         the new claimant is granted and the previous claimant's fingerprint and timestamp are replaced. Takeover
///         exists so a crashed claimant does not block its key forever, and it is the one place this store grants a
///         key twice. Keep the timeout longer than the longest mediation that claims through the store.
///     </para>
///     <para>
///         Completion and release are last-writer-wins on an unsettled claim. Release only deletes a claim that is
///         still unsettled when the session commits, so a release racing a completion cannot reopen an applied key.
///         A completion that lands after a takeover marks the new claimant's work applied, which is the late-settle
///         hazard the timeout guidance above exists to bound.
///     </para>
///     <para>
///         The store holds no mutable state and opens a new session per call, so one singleton instance serves every
///         mediation. The <c>PostgresException</c> type used to recognize a lost race arrives transitively with Marten;
///         this package adds no Npgsql reference of its own.
///     </para>
/// </remarks>
public sealed class MartenIdempotencyStore : IIdempotencyStore
{
    /// <summary>
    ///     Gets the document store each call opens its short claim session from.
    /// </summary>
    private readonly IDocumentStore _documentStore;

    /// <summary>
    ///     Gets the validated claim timeout configuration.
    /// </summary>
    private readonly MartenIdempotencyStoreOptions _options;

    /// <summary>
    ///     Gets the clock that stamps claims and measures their age against the timeout.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenIdempotencyStore" /> class.
    /// </summary>
    /// <param name="documentStore">The application-owned Marten document store.</param>
    /// <param name="options">The claim timeout configuration, or <see langword="null" /> for the defaults.</param>
    /// <param name="timeProvider">The clock that stamps claims, or <see langword="null" /> for <see cref="TimeProvider.System" />.</param>
    /// <exception cref="ArgumentOutOfRangeException"><c>options</c> carries a claim timeout that is zero or negative.</exception>
    public MartenIdempotencyStore(
        IDocumentStore documentStore,
        MartenIdempotencyStoreOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(documentStore);

        options ??= new MartenIdempotencyStoreOptions();
        options.Validate();

        _documentStore = documentStore;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        // A lost race retries against the winner, which by then is committed and readable: a new-key race lands as a
        // unique violation, a takeover race as a serialization failure. The loop ends when a session commits, and the
        // cancellation check keeps a cancelled mediation from claiming forever under contention.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var session = await _documentStore.LightweightSerializableSessionAsync(cancellationToken).ConfigureAwait(false);

                await using (session.ConfigureAwait(false))
                {
                    var now = _timeProvider.GetUtcNow();
                    var existing = await session.LoadAsync<MartenIdempotencyRecord>(key, cancellationToken).ConfigureAwait(false);

                    if (existing is null)
                    {
                        session.Store(new MartenIdempotencyRecord
                        {
                            Id = key,
                            Fingerprint = fingerprint,
                            ClaimedAt = now
                        });

                        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        return IdempotencyClaim.Granted;
                    }

                    if (existing.IsCompleted)
                    {
                        return FingerprintsMatch(existing.Fingerprint, fingerprint)
                            ? IdempotencyClaim.AlreadyCompleted(existing.Payload)
                            : IdempotencyClaim.FingerprintMismatch;
                    }

                    if (now - existing.ClaimedAt <= _options.ClaimTimeout)
                    {
                        return IdempotencyClaim.InFlight;
                    }

                    existing.Fingerprint = fingerprint;
                    existing.ClaimedAt = now;
                    session.Store(existing);

                    await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return IdempotencyClaim.Granted;
                }
            }
            catch (Exception exception) when (IsClaimConflict(exception))
            {
                // Another claimant committed first; the next pass reads the winner.
            }
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string key, string? payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var session = await _documentStore.LightweightSerializableSessionAsync(cancellationToken).ConfigureAwait(false);

                await using (session.ConfigureAwait(false))
                {
                    var now = _timeProvider.GetUtcNow();
                    var existing = await session.LoadAsync<MartenIdempotencyRecord>(key, cancellationToken).ConfigureAwait(false);

                    if (existing is null)
                    {
                        session.Store(new MartenIdempotencyRecord
                        {
                            Id = key,
                            IsCompleted = true,
                            Payload = payload,
                            ClaimedAt = now,
                            CompletedAt = now
                        });
                    }
                    else
                    {
                        existing.IsCompleted = true;
                        existing.Payload = payload;
                        existing.CompletedAt = now;
                        session.Store(existing);
                    }

                    await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception exception) when (IsClaimConflict(exception))
            {
                // A concurrent claimant or takeover committed first; the next pass completes over the winner.
            }
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var session = await _documentStore.LightweightSerializableSessionAsync(cancellationToken).ConfigureAwait(false);

                await using (session.ConfigureAwait(false))
                {
                    var existing = await session.LoadAsync<MartenIdempotencyRecord>(key, cancellationToken).ConfigureAwait(false);

                    // Only an unsettled claim is removed, and the serializable session makes the read and the delete
                    // atomic: a completion racing this delete fails one side with a conflict, and the retry re-reads.
                    if (existing is { IsCompleted: false })
                    {
                        session.Delete<MartenIdempotencyRecord>(key);
                        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }
            }
            catch (Exception exception) when (IsClaimConflict(exception))
            {
                // A concurrent settle committed first; the next pass re-reads before deciding to delete.
            }
        }
    }

    /// <summary>
    ///     Decides whether a stored fingerprint and an incoming one describe the same submission.
    /// </summary>
    /// <param name="stored">The fingerprint recorded when the key was claimed, when the claimant supplied one.</param>
    /// <param name="incoming">The fingerprint the repeat supplied, when it supplied one.</param>
    /// <returns>
    ///     <see langword="true" /> when the fingerprints match or either side is <see langword="null" />;
    ///     otherwise, <see langword="false" />.
    /// </returns>
    private static bool FingerprintsMatch(string? stored, string? incoming)
    {
        return stored is null || incoming is null || string.Equals(stored, incoming, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Decides whether a failure a claim session raised is a lost claim race worth retrying.
    /// </summary>
    /// <param name="exception">The failure a claim session raised, whatever wrapper Marten put around it.</param>
    /// <returns><see langword="true" /> when a storage error in the chain reports a claim conflict state.</returns>
    private static bool IsClaimConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && IsClaimConflictState(postgres.SqlState))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Decides whether a PostgreSQL state reports a lost claim race.
    /// </summary>
    /// <param name="sqlState">The five-character state the storage error reported.</param>
    /// <returns>
    ///     <see langword="true" /> for a unique violation (<c>23505</c>), a serialization failure (<c>40001</c>), or a
    ///     deadlock (<c>40P01</c>); otherwise, <see langword="false" />.
    /// </returns>
    private static bool IsClaimConflictState(string sqlState)
    {
        return sqlState is "23505" or "40001" or "40P01";
    }
}
