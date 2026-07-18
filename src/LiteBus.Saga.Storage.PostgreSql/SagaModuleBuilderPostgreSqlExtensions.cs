using LiteBus.Saga;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Provides PostgreSQL storage extensions for <see cref="SagaModuleBuilder" />.
/// </summary>
public static class SagaModuleBuilderPostgreSqlExtensions
{
    /// <summary>
    ///     Selects PostgreSQL saga storage for the current saga composition.
    /// </summary>
    /// <param name="builder">The saga module builder.</param>
    /// <param name="configure">The PostgreSQL saga storage configuration callback.</param>
    /// <returns>The current builder.</returns>
    public static SagaModuleBuilder UsePostgreSqlStorage(
        this SagaModuleBuilder builder,
        Action<PostgreSqlSagaModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterStorage(new PostgreSqlSagaModule(configure));
    }
}
