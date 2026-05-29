using System;
using Npgsql;

namespace LiteBus.Outbox.PostgreSql;

/// <summary>
///     Holds the PostgreSQL data source and options registered for the outbox store.
/// </summary>
/// <remarks>
///     This registration is consumed by optional schema bootstrap hosting and is registered automatically by
///     <see cref="PostgreSqlOutboxModule" />.
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
}
