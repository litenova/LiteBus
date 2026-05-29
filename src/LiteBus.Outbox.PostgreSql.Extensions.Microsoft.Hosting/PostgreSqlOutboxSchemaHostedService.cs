using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Outbox.PostgreSql.Extensions.Microsoft.Hosting;

/// <summary>
///     Ensures the PostgreSQL outbox schema exists and matches the expected version before other hosted services continue
///     startup.
/// </summary>
/// <remarks>
///     Register this hosted service before outbox processor hosting so schema bootstrap completes first. The service no-ops
///     when <see cref="PostgreSqlOutboxStoreOptions.EnsureSchemaCreationOnStartup" /> is <see langword="false" />.
/// </remarks>
public sealed class PostgreSqlOutboxSchemaHostedService : IHostedService
{
    /// <summary>
    ///     The registered outbox store configuration consumed during host startup.
    /// </summary>
    private readonly PostgreSqlOutboxStoreRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxSchemaHostedService" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL outbox store configuration.</param>
    public PostgreSqlOutboxSchemaHostedService(PostgreSqlOutboxStoreRegistration registration)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_registration.Options.EnsureSchemaCreationOnStartup)
        {
            return;
        }

        await PostgreSqlOutboxSchema.EnsureAsync(
                _registration.DataSource,
                _registration.Options,
                cancellationToken)
            .ConfigureAwait(false);

        if (_registration.Options.ValidateSchemaCreationOnStartup)
        {
            await PostgreSqlOutboxSchema.ValidateAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
