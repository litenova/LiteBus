using System;

namespace LiteBus.PostgreSql;

/// <summary>
///     A no-op logger used when schema operations do not configure logging explicitly.
/// </summary>
public sealed class NullPostgreSqlSchemaLogger : IPostgreSqlSchemaLogger
{
    /// <summary>
    ///     Gets the shared null logger instance.
    /// </summary>
    public static NullPostgreSqlSchemaLogger Instance { get; } = new();

    private NullPostgreSqlSchemaLogger()
    {
    }

    /// <inheritdoc />
    public void Log(PostgreSqlSchemaLogLevel level, string message, Exception? exception = null)
    {
    }
}
