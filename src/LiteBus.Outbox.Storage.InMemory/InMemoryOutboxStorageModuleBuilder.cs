using System;

namespace LiteBus.Outbox.Storage.InMemory;

/// <summary>
///     Configures the in-memory outbox store module.
/// </summary>
public sealed class InMemoryOutboxStorageModuleBuilder
{
    /// <summary>
    ///     Gets the in-memory store options.
    /// </summary>
    public InMemoryOutboxStoreOptions Options { get; private set; } = new();

    /// <summary>
    ///     Gets the optional time provider registered with the dependency container.
    /// </summary>
    public TimeProvider? TimeProvider { get; private set; }

    /// <summary>
    ///     Replaces the in-memory store options.
    /// </summary>
    /// <param name="options">The store options.</param>
    /// <returns>The current builder.</returns>
    public InMemoryOutboxStorageModuleBuilder UseOptions(InMemoryOutboxStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Options = options;
        return this;
    }

    /// <summary>
    ///     Sets the time provider used for lease expiry when lease requests omit an explicit clock value.
    /// </summary>
    /// <param name="timeProvider">The time provider instance.</param>
    /// <returns>The current builder.</returns>
    public InMemoryOutboxStorageModuleBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        TimeProvider = timeProvider;
        return this;
    }
}