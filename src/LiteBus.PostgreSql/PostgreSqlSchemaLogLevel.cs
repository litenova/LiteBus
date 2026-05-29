namespace LiteBus.PostgreSql;

/// <summary>
///     Log levels used by <see cref="IPostgreSqlSchemaLogger" />.
/// </summary>
public enum PostgreSqlSchemaLogLevel
{
    /// <summary>
    ///     Detailed diagnostic information.
    /// </summary>
    Debug = 0,

    /// <summary>
    ///     General operational information.
    /// </summary>
    Information = 1,

    /// <summary>
    ///     Abnormal but non-fatal conditions.
    /// </summary>
    Warning = 2,

    /// <summary>
    ///     Failures that stop schema creation or validation.
    /// </summary>
    Error = 3
}
