using LiteBus.Runtime.Abstractions;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Ensures the PostgreSQL saga schema exists during host startup when configured to do so.
/// </summary>
public sealed class PostgreSqlSagaSchemaInitializer : IStartupTask
{
    /// <summary>
    ///     The registered saga store configuration consumed during host startup.
    /// </summary>
    private readonly PostgreSqlSagaStoreRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSagaSchemaInitializer" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL saga store configuration.</param>
    public PostgreSqlSagaSchemaInitializer(PostgreSqlSagaStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registration = registration;
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_registration.Options.EnsureSchemaCreationOnStartup)
        {
            await PostgreSqlSagaSchema.EnsureAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (_registration.Options.ValidateSchemaCreationOnStartup)
        {
            await PostgreSqlSagaSchema.ValidateAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}