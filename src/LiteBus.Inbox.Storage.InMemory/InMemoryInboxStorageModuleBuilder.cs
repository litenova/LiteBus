using System;

namespace LiteBus.Inbox.Storage.InMemory;

/// <summary>
///     Configures the in-memory command inbox store module.
/// </summary>
/// <example>
///     Register with default options for tests:
///     <code>
/// liteBus.AddInMemoryInboxStorage();
///     </code>
///     Register with a bounded capacity and custom lease defaults:
///     <code>
/// liteBus.AddInMemoryInboxStorage(storage =>
/// {
///     storage.UseOptions(new InMemoryInboxStoreOptions
///     {
///         Capacity = 1000,
///         DefaultLeaseDuration = TimeSpan.FromSeconds(30)
///     });
///     storage.UseTimeProvider(TimeProvider.System);
/// });
///     </code>
/// </example>
public sealed class InMemoryInboxStorageModuleBuilder
{
    /// <summary>
    ///     Gets the in-memory store options.
    /// </summary>
    public InMemoryInboxStoreOptions Options { get; private set; } = new();

    /// <summary>
    ///     Gets the optional time provider registered with the dependency container.
    /// </summary>
    public TimeProvider? TimeProvider { get; private set; }

    /// <summary>
    ///     Replaces the in-memory store options.
    /// </summary>
    /// <param name="options">The store options.</param>
    /// <returns>The current builder.</returns>
    public InMemoryInboxStorageModuleBuilder UseOptions(InMemoryInboxStoreOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    /// <summary>
    ///     Sets the time provider used for lease expiry when lease requests omit an explicit clock value.
    /// </summary>
    /// <param name="timeProvider">The time provider instance.</param>
    /// <returns>The current builder.</returns>
    public InMemoryInboxStorageModuleBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        TimeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        return this;
    }
}
