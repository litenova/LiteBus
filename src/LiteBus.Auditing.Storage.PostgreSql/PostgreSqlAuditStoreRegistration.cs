using System;
using Npgsql;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Captures PostgreSQL audit store configuration for schema startup tasks and diagnostic probes.
/// </summary>
public sealed class PostgreSqlAuditStoreRegistration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditStoreRegistration" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The audit store options.</param>
    public PostgreSqlAuditStoreRegistration(NpgsqlDataSource dataSource, PostgreSqlAuditStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        DataSource = dataSource;
        Options = options;
    }

    /// <summary>
    ///     Gets the PostgreSQL data source.
    /// </summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>
    ///     Gets the audit store options.
    /// </summary>
    public PostgreSqlAuditStoreOptions Options { get; }
}
