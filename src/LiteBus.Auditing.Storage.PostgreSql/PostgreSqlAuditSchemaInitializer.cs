using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Ensures the PostgreSQL audit schema exists during host startup when configured to do so.
/// </summary>
public sealed class PostgreSqlAuditSchemaInitializer : IStartupTask
{
    /// <summary>
    ///     The data source owner resolved so the container creates and later disposes a module-owned data source.
    /// </summary>
    private readonly PostgreSqlDataSourceOwner<PostgreSqlAuditModule> _dataSourceOwner;

    /// <summary>
    ///     The registered audit store configuration consumed during host startup.
    /// </summary>
    private readonly PostgreSqlAuditStoreRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditSchemaInitializer" /> class.
    /// </summary>
    /// <param name="dataSourceOwner">
    ///     The module's data source owner. Depending on it makes the container create the owner when this initializer
    ///     runs, so container disposal disposes a module-owned data source even when no audit record is ever written.
    /// </param>
    /// <param name="registration">The registered PostgreSQL audit store configuration.</param>
    public PostgreSqlAuditSchemaInitializer(
        PostgreSqlDataSourceOwner<PostgreSqlAuditModule> dataSourceOwner,
        PostgreSqlAuditStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(dataSourceOwner);
        ArgumentNullException.ThrowIfNull(registration);
        _dataSourceOwner = dataSourceOwner;
        _registration = registration;
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_registration.Options.EnsureSchemaCreationOnStartup)
        {
            await PostgreSqlAuditSchema.EnsureAsync(
                    _dataSourceOwner.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (_registration.Options.ValidateSchemaCreationOnStartup)
        {
            await PostgreSqlAuditSchema.ValidateAsync(
                    _dataSourceOwner.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
