using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Optional retention settings shared by inbox and outbox stores.
/// </summary>
public interface IMessageStoreRetentionOptions
{
    /// <summary>
    ///     Gets the duration completed or published rows remain before cleanup may delete them.
    /// </summary>
    /// <value>
    ///     When <see langword="null" />, terminal rows are not deleted automatically.
    /// </value>
    TimeSpan? TerminalRetention { get; }
}