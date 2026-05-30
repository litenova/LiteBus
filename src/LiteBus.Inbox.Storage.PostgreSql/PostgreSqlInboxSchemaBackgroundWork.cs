using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Ensures the PostgreSQL inbox schema exists during host startup when configured to do so.
/// </summary>
public sealed class PostgreSqlInboxSchemaBackgroundWork : ILiteBusBackgroundWork
{
    /// <summary>
    ///     The registered inbox store configuration consumed during host startup.
    /// </summary>
    private readonly PostgreSqlInboxStoreRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxSchemaBackgroundWork" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL inbox store configuration.</param>
    public PostgreSqlInboxSchemaBackgroundWork(PostgreSqlInboxStoreRegistration registration)
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
}
