using System;

namespace LiteBus.Outbox.Storage.PostgreSql.Exceptions;

/// <summary>
///     Thrown when PostgreSQL outbox storage module configuration is invalid.
/// </summary>
public sealed class OutboxPostgreSqlStorageConfigurationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxPostgreSqlStorageConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public OutboxPostgreSqlStorageConfigurationException(string message)
        : base(message)
    {
    }
}
