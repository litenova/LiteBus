using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Ensures the PostgreSQL outbox schema exists during host startup when configured to do so.
/// </summary>
public sealed class PostgreSqlOutboxSchemaBackgroundWork : ILiteBusBackgroundWork
{
    /// <summary>
    ///     The registered outbox store configuration consumed during host startup.
    /// </summary>
    private readonly PostgreSqlOutboxStoreRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxSchemaBackgroundWork" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL outbox store configuration.</param>
    public PostgreSqlOutboxSchemaBackgroundWork(PostgreSqlOutboxStoreRegistration registration)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
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
}
