using System.Collections.Concurrent;
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
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    /// <summary>
    ///     The applied keys and the payloads recorded for them.
    /// </summary>
    private readonly ConcurrentDictionary<string, string?> _applied = new(StringComparer.Ordinal);

    /// <summary>
    ///     The keys claimed by a mediation that has not settled yet.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _claimed = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the keys recorded as applied.
    /// </summary>
    public IReadOnlyCollection<string> AppliedKeys => _applied.Keys.ToList();

    /// <inheritdoc />
    public Task<IdempotencyClaim> TryClaimAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_applied.TryGetValue(key, out var payload))
        {
            return Task.FromResult(IdempotencyClaim.AlreadyCompleted(payload));
        }

        // TryAdd is the atomic claim, which is what a primary-key insert gives a real store.
        _claimed.TryAdd(key, 0);
        return Task.FromResult(IdempotencyClaim.Granted);
    }

    /// <inheritdoc />
    public Task CompleteAsync(string key, string? payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _applied[key] = payload;
        _claimed.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _claimed.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
