using System;
using Npgsql;

namespace LiteBus.PostgreSql;

/// <summary>
///     Creates <see cref="NpgsqlDataSource" /> instances for LiteBus PostgreSQL store registration.
/// </summary>
/// <remarks>
///     Prefer registering a shared <see cref="NpgsqlDataSource" /> built by your application when you already use
///     Npgsql 7+ data sources for pooling and configuration. Use
///     <see cref="CreateFromConnectionString(string)" /> for simpler setups such as tests, samples, and small services.
/// </remarks>
public static class PostgreSqlDataSourceFactory
{
    /// <summary>
    ///     Creates a new <see cref="NpgsqlDataSource" /> from a PostgreSQL connection string.
    /// </summary>
    /// <param name="connectionString">
    ///     A PostgreSQL connection string, for example
    ///     <c>Host=localhost;Database=orders;Username=app;Password=secret</c>.
    /// </param>
    /// <returns>A data source that can be passed to LiteBus PostgreSQL store builders.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="connectionString" /> is null or whitespace.</exception>
    /// <example>
    ///     <code>
    /// liteBus.AddPostgreSqlCommandInboxStore(postgres =>
    /// {
    ///     postgres.UseConnectionString(configuration.GetConnectionString("OrdersDb")!);
    /// });
    ///     </code>
    /// </example>
    public static NpgsqlDataSource CreateFromConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return NpgsqlDataSource.Create(connectionString);
    }
}
