using System;
using Npgsql;
using NpgsqlTypes;

namespace LiteBus.Storage.PostgreSql.Stores;

/// <summary>
///     Shared Npgsql parameter helpers for PostgreSQL storage adapters.
/// </summary>
internal static class PostgreSqlParameterExtensions
{
    /// <summary>
    ///     Adds a UUID array parameter with an explicit PostgreSQL array type.
    /// </summary>
    /// <param name="command">The command receiving the parameter.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="values">The UUID values bound to the parameter.</param>
    internal static void AddUuidArrayParameter(NpgsqlCommand command, string name, Guid[] values)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = values
        });
    }

    /// <summary>
    ///     Adds a nullable text array parameter with an explicit PostgreSQL array type.
    /// </summary>
    /// <param name="command">The command receiving the parameter.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="values">The text values bound to the parameter.</param>
    internal static void AddTextArrayParameter(NpgsqlCommand command, string name, string?[] values)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = values
        });
    }
}