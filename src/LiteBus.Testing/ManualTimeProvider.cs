using System;

namespace LiteBus.Testing;

/// <summary>
///     A controllable UTC clock for deterministic inbox, outbox, and storage tests.
/// </summary>
/// <remarks>
///     All members are thread-safe, so a test thread may advance the clock while processor passes read it.
/// </remarks>
public sealed class ManualTimeProvider : TimeProvider
{
    /// <summary>
    ///     The current UTC timestamp returned by <see cref="GetUtcNow" />.
    /// </summary>
    private DateTimeOffset _utcNow;

    /// <summary>
    ///     Gets the lock that serializes clock reads and writes.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="ManualTimeProvider" /> class.
    /// </summary>
    /// <param name="initial">The initial UTC timestamp.</param>
    public ManualTimeProvider(DateTimeOffset initial)
    {
        _utcNow = initial;
    }

    /// <summary>
    ///     Advances the clock by the supplied duration.
    /// </summary>
    /// <param name="amount">The duration to add.</param>
    public void Advance(TimeSpan amount)
    {
        lock (_sync)
        {
            _utcNow = _utcNow.Add(amount);
        }
    }

    /// <summary>
    ///     Replaces the current UTC timestamp.
    /// </summary>
    /// <param name="value">The new UTC timestamp.</param>
    public void SetUtcNow(DateTimeOffset value)
    {
        lock (_sync)
        {
            _utcNow = value;
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _utcNow;
        }
    }
}
