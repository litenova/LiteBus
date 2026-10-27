using System;
using Npgsql;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Captures PostgreSQL recurring lease configuration for schema startup tasks.
/// </summary>
public sealed class PostgreSqlRecurringLeaseRegistration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlRecurringLeaseRegistration" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The recurring lease options.</param>
    public PostgreSqlRecurringLeaseRegistration(NpgsqlDataSource dataSource, PostgreSqlRecurringLeaseOptions options)
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
    ///     Gets the recurring lease options.
    /// </summary>
    public PostgreSqlRecurringLeaseOptions Options { get; }
}
