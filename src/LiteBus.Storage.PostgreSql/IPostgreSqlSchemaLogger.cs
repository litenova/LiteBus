using System;

namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Receives log messages from LiteBus PostgreSQL schema creation, upgrade, and validation operations.
/// </summary>
/// <remarks>
///     Assign an implementation through <see cref="PostgreSqlSchemaStoreOptions.Logger" />. When no logger is supplied,
///     schema operations run silently. Hosting applications can bridge this interface to <c>ILogger&lt;T&gt;</c> without
///     adding logging package dependencies to <c>LiteBus.Storage.PostgreSql</c>.
/// </remarks>
/// <example>
///     <code>
/// var options = new PostgreSqlInboxStoreOptions
/// {
///     Logger = new ConsolePostgreSqlSchemaLogger()
/// };
///
/// await PostgreSqlInboxSchema.EnsureAsync(dataSource, options, cancellationToken);
///     </code>
/// </example>
public interface IPostgreSqlSchemaLogger
{
    /// <summary>
    ///     Writes one schema operation log entry.
    /// </summary>
    /// <param name="level">The log level.</param>
    /// <param name="message">The message text.</param>
    /// <param name="exception">An optional exception associated with the entry.</param>
    void Log(PostgreSqlSchemaLogLevel level, string message, Exception? exception = null);
}
