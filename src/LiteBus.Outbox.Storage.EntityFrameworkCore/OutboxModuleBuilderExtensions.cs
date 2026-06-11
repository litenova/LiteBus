using System;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Registers Entity Framework Core outbox storage through <see cref="OutboxModuleBuilder" />.
/// </summary>
public static class OutboxModuleBuilderEfCoreExtensions
{
    /// <summary>
    ///     Registers the EF Core outbox store as an outbox child module.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The EF Core store configuration action.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UseEfCoreStorage(
        this OutboxModuleBuilder builder,
        Action<EfCoreOutboxStorageModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterStorage(new EfCoreOutboxStorageModule(configure));
    }
}