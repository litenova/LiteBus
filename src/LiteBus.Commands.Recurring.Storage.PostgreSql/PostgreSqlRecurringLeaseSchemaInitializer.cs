using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Ensures the PostgreSQL recurring lease schema exists during host startup when configured to do so.
/// </summary>
public sealed class PostgreSqlRecurringLeaseSchemaInitializer : IStartupTask
{
    /// <summary>
    ///     The data source owner resolved so the container creates and later disposes a module-owned data source.
    /// </summary>
    private readonly PostgreSqlDataSourceOwner<PostgreSqlRecurringLeaseModule> _dataSourceOwner;

    /// <summary>
    ///     The registered recurring lease configuration consumed during host startup.
    /// </summary>
    private readonly PostgreSqlRecurringLeaseRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlRecurringLeaseSchemaInitializer" /> class.
    /// </summary>
    /// <param name="dataSourceOwner">
    ///     The module's data source owner. Depending on it makes the container create the owner when this initializer
    ///     runs, so container disposal disposes a module-owned data source even when no loop ever resolves the lease.
    /// </param>
    /// <param name="registration">The registered PostgreSQL recurring lease configuration.</param>
    public PostgreSqlRecurringLeaseSchemaInitializer(
        PostgreSqlDataSourceOwner<PostgreSqlRecurringLeaseModule> dataSourceOwner,
        PostgreSqlRecurringLeaseRegistration registration)
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
            await PostgreSqlRecurringLeaseSchema.EnsureAsync(
                    _dataSourceOwner.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (_registration.Options.ValidateSchemaCreationOnStartup)
        {
            await PostgreSqlRecurringLeaseSchema.ValidateAsync(
                    _dataSourceOwner.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
