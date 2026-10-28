using System;

namespace LiteBus.Commands.Storage.PostgreSql;

/// <summary>
///     Registers PostgreSQL idempotency storage through <see cref="CommandModuleBuilder" />.
/// </summary>
public static class CommandModuleBuilderPostgreSqlExtensions
{
    /// <summary>
    ///     Registers the PostgreSQL idempotency store as a command child module.
    /// </summary>
    /// <param name="builder">The command module builder.</param>
    /// <param name="configure">The PostgreSQL idempotency store configuration action.</param>
    /// <returns>The command module builder for chaining.</returns>
    public static CommandModuleBuilder UsePostgreSqlIdempotencyStore(
        this CommandModuleBuilder builder,
        Action<PostgreSqlIdempotencyModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterChildModule(new PostgreSqlIdempotencyModule(configure));
    }
}
