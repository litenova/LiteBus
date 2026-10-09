using System;

namespace LiteBus.Inbox.Storage.PostgreSql.Exceptions;

/// <summary>
///     Thrown when PostgreSQL inbox storage module configuration is invalid.
/// </summary>
public sealed class InboxPostgreSqlStorageConfigurationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxPostgreSqlStorageConfigurationException" /> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InboxPostgreSqlStorageConfigurationException(string message)
        : base(message)
    {
    }
}
