using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Ensures the PostgreSQL recurring lease schema exists during host startup when configured to do so.
/// </summary>
public sealed class PostgreSqlRecurringLeaseSchemaInitializer : IStartupTask
{
    /// <summary>
    ///     The registered recurring lease configuration consumed during host startup.
    /// </summary>
    private readonly PostgreSqlRecurringLeaseRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlRecurringLeaseSchemaInitializer" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL recurring lease configuration.</param>
    public PostgreSqlRecurringLeaseSchemaInitializer(PostgreSqlRecurringLeaseRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registration = registration;
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_registration.Options.EnsureSchemaCreationOnStartup)
        {
            await PostgreSqlRecurringLeaseSchema.EnsureAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (_registration.Options.ValidateSchemaCreationOnStartup)
        {
            await PostgreSqlRecurringLeaseSchema.ValidateAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
