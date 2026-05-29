using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Inbox.PostgreSql.Extensions.Microsoft.Hosting;

/// <summary>
///     Ensures the PostgreSQL command inbox schema exists and matches the expected version before other hosted services
///     continue startup.
/// </summary>
/// <remarks>
///     Register this hosted service before inbox processor hosting so schema bootstrap completes first. The service no-ops
///     when <see cref="PostgreSqlInboxStoreOptions.EnsureSchemaCreationOnStartup" /> is <see langword="false" />.
/// </remarks>
public sealed class PostgreSqlInboxSchemaHostedService : IHostedService
{
    /// <summary>
    ///     The registered inbox store configuration consumed during host startup.
    /// </summary>
    private readonly PostgreSqlInboxStoreRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxSchemaHostedService" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL inbox store configuration.</param>
    public PostgreSqlInboxSchemaHostedService(PostgreSqlInboxStoreRegistration registration)
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

        await PostgreSqlInboxSchema.EnsureAsync(
                _registration.DataSource,
                _registration.Options,
                cancellationToken)
            .ConfigureAwait(false);

        if (_registration.Options.ValidateSchemaCreationOnStartup)
        {
            await PostgreSqlInboxSchema.ValidateAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
