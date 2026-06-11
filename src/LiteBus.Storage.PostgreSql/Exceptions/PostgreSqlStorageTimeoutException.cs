using System;

namespace LiteBus.Storage.PostgreSql.Exceptions;

/// <summary>
///     Thrown when a PostgreSQL storage operation exceeds its allowed wait time.
/// </summary>
public sealed class PostgreSqlStorageTimeoutException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlStorageTimeoutException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PostgreSqlStorageTimeoutException(string message)
        : base(message)
    {
    }
}