using System;
using LiteBus.Inbox.Abstractions;
using Npgsql;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Holds the PostgreSQL data source and options registered for the inbox store.
/// </summary>
/// <remarks>
///     This registration is consumed by optional schema bootstrap hosting, ambient transactional writers, and is
///     registered
///     automatically by <see cref="PostgreSqlInboxModule" />.
/// </remarks>
public sealed class PostgreSqlInboxStoreRegistration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxStoreRegistration" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The inbox store options.</param>
    public PostgreSqlInboxStoreRegistration(NpgsqlDataSource dataSource, PostgreSqlInboxStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DataSource = dataSource;
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
    }

    /// <summary>
    ///     Gets the PostgreSQL data source used by the inbox store.
    /// </summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>
    ///     Gets the inbox store options.
    /// </summary>
    public PostgreSqlInboxStoreOptions Options { get; }

    /// <summary>
    ///     Creates an inbox writer bound to an existing PostgreSQL connection and transaction.
    /// </summary>
    /// <param name="connection">The existing open connection owned by the caller.</param>
    /// <param name="transaction">The transaction that should contain inbox writes.</param>
    /// <returns>A transactional inbox store participating in the supplied transaction.</returns>
    public ITransactionalInboxStore CreateTransactionalStore(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var store = new PostgreSqlInboxStore(DataSource, Options);
        return store.UseExistingConnection(connection, transaction);
    }
}