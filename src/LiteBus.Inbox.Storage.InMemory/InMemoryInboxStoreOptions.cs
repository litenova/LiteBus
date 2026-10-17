using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.Storage.InMemory;

/// <summary>
///     Configures the in-memory inbox store.
/// </summary>
public sealed record InMemoryInboxStoreOptions : IMessageStoreRetentionOptions
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
    ///     Gets the lease duration value validated at construction.
    /// </summary>
    /// <value>
    ///     The store requires a positive value but never applies it to a lease. <see cref="InMemoryInboxStore.LeasePendingAsync" />
    ///     requires a positive duration on every request and throws <see cref="ArgumentOutOfRangeException" /> for a zero
    ///     duration. Defaults to one minute.
    /// </value>
    public TimeSpan DefaultLeaseDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public TimeSpan? TerminalRetention { get; init; }
}
