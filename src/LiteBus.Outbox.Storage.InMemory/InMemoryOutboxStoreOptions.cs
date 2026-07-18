using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.Storage.InMemory;

/// <summary>
///     Configures the in-memory outbox store.
/// </summary>
public sealed record InMemoryOutboxStoreOptions : IMessageStoreRetentionOptions
{
    /// <summary>
    ///     Gets the maximum number of messages retained by the store.
    /// </summary>
    /// <value>
    ///     When zero, the store accepts messages without a capacity limit. When positive, new messages are rejected once
    ///     the store reaches this count unless the submission is idempotent.
    /// </value>
    public int Capacity { get; init; }

    /// <summary>
    ///     Gets the default publication lease duration used when a lease request supplies a zero duration.
    /// </summary>
    public TimeSpan DefaultLeaseDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public TimeSpan? TerminalRetention { get; init; }
}