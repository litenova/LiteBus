using System;

namespace LiteBus.Inbox.Storage.InMemory;

/// <summary>
///     Configures the in-memory command inbox store.
/// </summary>
public sealed record InMemoryInboxStoreOptions
{
    /// <summary>
    ///     Gets the maximum number of commands retained by the store.
    /// </summary>
    /// <value>
    ///     When zero, the store accepts commands without a capacity limit. When positive, new commands are rejected once
    ///     the store reaches this count unless the submission is idempotent.
    /// </value>
    public int Capacity { get; init; }

    /// <summary>
    ///     Gets the default processing lease duration used when a lease request supplies a zero duration.
    /// </summary>
    public TimeSpan DefaultLeaseDuration { get; init; } = TimeSpan.FromMinutes(1);
}
