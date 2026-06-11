using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Storage.InMemory;

/// <summary>
///     Registers in-memory inbox storage through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderInMemoryExtensions
{
    /// <summary>
    ///     Registers the in-memory inbox store as an inbox child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">An optional in-memory store configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseInMemoryStorage(
        this InboxModuleBuilder builder,
        Action<InMemoryInboxStorageModuleBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.RegisterStorage(new InMemoryInboxStorageModule(configure ??
                                                                      (_ =>
                                                                      {
                                                                      })));
    }
}