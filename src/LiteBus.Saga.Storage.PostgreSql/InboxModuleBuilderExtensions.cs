using LiteBus.Inbox.Abstractions;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Provides PostgreSQL saga storage extensions for <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderExtensions
{
    /// <summary>
    ///     Registers PostgreSQL saga storage for inbox saga support.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The PostgreSQL saga storage configuration callback.</param>
    /// <returns>The current builder.</returns>
    public static InboxModuleBuilder UsePostgreSqlSagaStorage(
        this InboxModuleBuilder builder,
        Action<PostgreSqlSagaModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterSaga(new PostgreSqlSagaModule(configure));
    }
}
