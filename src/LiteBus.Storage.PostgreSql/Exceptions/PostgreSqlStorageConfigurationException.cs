using System;

namespace LiteBus.Storage.PostgreSql.Exceptions;

/// <summary>
///     Thrown when PostgreSQL storage configuration or embedded SQL resources are invalid.
/// </summary>
public sealed class PostgreSqlStorageConfigurationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlStorageConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PostgreSqlStorageConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlStorageConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public PostgreSqlStorageConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
