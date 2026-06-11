using System;
using LiteBus.Outbox.Abstractions;
using Npgsql;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Holds the PostgreSQL data source and options registered for the outbox store.
/// </summary>
/// <remarks>
///     This registration is consumed by optional schema bootstrap hosting, ambient transactional writers, and is registered
///     automatically by <see cref="PostgreSqlOutboxModule" />.
/// </remarks>
public sealed class PostgreSqlOutboxStoreRegistration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxStoreRegistration" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The outbox store options.</param>
    public PostgreSqlOutboxStoreRegistration(NpgsqlDataSource dataSource, PostgreSqlOutboxStoreOptions options)
    {
        DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    ///     Gets the PostgreSQL data source used by the outbox store.
    /// </summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>
    ///     Gets the outbox store options.
    /// </summary>
    public PostgreSqlOutboxStoreOptions Options { get; }

    /// <summary>
    ///     Creates an outbox writer bound to an existing PostgreSQL connection and transaction.
    /// </summary>
    /// <param name="connection">The existing open connection owned by the caller.</param>
    /// <param name="transaction">The transaction that should contain outbox writes.</param>
    /// <returns>A transactional outbox store participating in the supplied transaction.</returns>
    public ITransactionalOutboxStore CreateTransactionalStore(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var store = new PostgreSqlOutboxStore(DataSource, Options);
        return store.UseExistingConnection(connection, transaction);
    }
}
