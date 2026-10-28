using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Commands.Storage.PostgreSql;

/// <summary>
///     Ensures the PostgreSQL idempotency schema exists during host startup when configured to do so.
/// </summary>
public sealed class PostgreSqlIdempotencySchemaInitializer : IStartupTask
{
    /// <summary>
    ///     The registered idempotency store configuration consumed during host startup.
    /// </summary>
    private readonly PostgreSqlIdempotencyStoreRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlIdempotencySchemaInitializer" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL idempotency store configuration.</param>
    public PostgreSqlIdempotencySchemaInitializer(PostgreSqlIdempotencyStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registration = registration;
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_registration.Options.EnsureSchemaCreationOnStartup)
        {
            await PostgreSqlIdempotencySchema.EnsureAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (_registration.Options.ValidateSchemaCreationOnStartup)
        {
            await PostgreSqlIdempotencySchema.ValidateAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
