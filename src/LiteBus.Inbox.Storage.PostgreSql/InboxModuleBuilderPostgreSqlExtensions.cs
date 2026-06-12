using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Registers PostgreSQL inbox storage through <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderPostgreSqlExtensions
{
    /// <summary>
    ///     Registers the PostgreSQL inbox store as an inbox child module.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The PostgreSQL store configuration action.</param>
    /// <returns>The inbox module builder for chaining.</returns>
    public static InboxModuleBuilder UsePostgreSqlStorage(
        this InboxModuleBuilder builder,
        Action<PostgreSqlInboxModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterStorage(new PostgreSqlInboxModule(configure));
    }
}