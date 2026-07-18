using System;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Registers PostgreSQL outbox storage through <see cref="OutboxModuleBuilder" />.
/// </summary>
public static class OutboxModuleBuilderPostgreSqlExtensions
{
    /// <summary>
    ///     Registers the PostgreSQL outbox store as an outbox child module.
    /// </summary>
    /// <param name="builder">The outbox module builder.</param>
    /// <param name="configure">The PostgreSQL store configuration action.</param>
    /// <returns>The outbox module builder for chaining.</returns>
    public static OutboxModuleBuilder UsePostgreSqlStorage(
        this OutboxModuleBuilder builder,
        Action<PostgreSqlOutboxModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterStorage(new PostgreSqlOutboxModule(configure));
    }
}