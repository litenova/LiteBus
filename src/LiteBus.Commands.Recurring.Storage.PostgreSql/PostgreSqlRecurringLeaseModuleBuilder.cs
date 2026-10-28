using System;
using Npgsql;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Configures the PostgreSQL recurring command lease module.
/// </summary>
public sealed class PostgreSqlRecurringLeaseModuleBuilder
{
    /// <summary>
    ///     Gets the configured PostgreSQL data source.
    /// </summary>
    internal NpgsqlDataSource? DataSource { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this builder created the data source and owns disposal.
    /// </summary>
    internal bool OwnsDataSource { get; private set; }

    /// <summary>
    ///     Gets the recurring lease options.
    /// </summary>
    public PostgreSqlRecurringLeaseOptions Options { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether schema initialization is registered.
    /// </summary>
    internal bool IsSchemaInitializationEnabled { get; private set; } = true;

    /// <summary>
    ///     Uses an existing PostgreSQL data source.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dataSource" /> is <see langword="null" />.</exception>
    public PostgreSqlRecurringLeaseModuleBuilder UseDataSource(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DataSource = dataSource;
        OwnsDataSource = false;
        return this;
    }

    /// <summary>
    ///     Creates a PostgreSQL data source from a connection string.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="connectionString" /> is null, empty, or whitespace.</exception>
    public PostgreSqlRecurringLeaseModuleBuilder UseConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        DataSource = NpgsqlDataSource.Create(connectionString);
        OwnsDataSource = true;
        return this;
    }

    /// <summary>
    ///     Replaces the default recurring lease options.
    /// </summary>
    /// <param name="options">The recurring lease options.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
    public PostgreSqlRecurringLeaseModuleBuilder UseOptions(PostgreSqlRecurringLeaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        return this;
    }

    /// <summary>
    ///     Disables automatic schema initialization on host startup.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Use this when a migration pipeline owns the leases table. The table must still exist before the first pass
    ///     acquires a lease, or acquisition fails.
    /// </remarks>
    public PostgreSqlRecurringLeaseModuleBuilder DisableSchemaInitialization()
    {
        IsSchemaInitializationEnabled = false;
        return this;
    }
}
