using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Registers Entity Framework Core inbox storage through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderEfCoreExtensions
{
    /// <summary>
    ///     Registers the EF Core inbox store as an inbox child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The EF Core store configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UseEntityFrameworkCoreStorage(
        this InboxModuleBuilder builder,
        Action<EfCoreInboxStorageModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterStorage(new EfCoreInboxStorageModule(configure));
    }
}