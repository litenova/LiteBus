using System;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.InMemory;

/// <summary>
///     Registers in-memory outbox storage through <see cref="OutboxModuleBuilder" />.
/// </summary>
public static class OutboxModuleBuilderInMemoryExtensions
{
    /// <summary>
    ///     Registers the in-memory outbox store as an outbox child module.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">An optional in-memory store configuration action.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseInMemoryStorage(
        this OutboxModuleBuilder builder,
        Action<InMemoryOutboxStorageModuleBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RegisterStorage(new InMemoryOutboxStorageModule(configure ?? (_ => { })));
    }
}
