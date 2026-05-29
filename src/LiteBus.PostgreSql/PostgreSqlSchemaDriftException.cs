using System;

namespace LiteBus.PostgreSql;

/// <summary>
///     Thrown when a LiteBus PostgreSQL store table does not match the schema version expected by the library.
/// </summary>
public sealed class PostgreSqlSchemaDriftException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSchemaDriftException" /> class.
    /// </summary>
    /// <param name="component">The LiteBus store component that failed validation.</param>
    /// <param name="schemaName">The PostgreSQL schema name of the validated table.</param>
    /// <param name="tableName">The PostgreSQL table name of the validated table.</param>
    /// <param name="expectedVersion">The schema version expected by the library.</param>
    /// <param name="actualVersion">The schema version recorded in metadata, if any.</param>
    /// <param name="details">Additional validation details.</param>
    public PostgreSqlSchemaDriftException(
        string component,
        string schemaName,
        string tableName,
        int expectedVersion,
        int? actualVersion,
        string details)
        : base(BuildMessage(component, schemaName, tableName, expectedVersion, actualVersion, details))
    {
        Component = component;
        SchemaName = schemaName;
        TableName = tableName;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
        Details = details;
    }

    /// <summary>
    ///     Gets the LiteBus store component that failed validation.
    /// </summary>
    public string Component { get; }

    /// <summary>
    ///     Gets the PostgreSQL schema name of the validated table.
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    ///     Gets the PostgreSQL table name of the validated table.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    ///     Gets the schema version expected by the library.
    /// </summary>
    public int ExpectedVersion { get; }

    /// <summary>
    ///     Gets the schema version recorded in metadata, if any.
    /// </summary>
    public int? ActualVersion { get; }

    /// <summary>
    ///     Gets additional validation details.
    /// </summary>
    public string Details { get; }

    private static string BuildMessage(
        string component,
        string schemaName,
        string tableName,
        int expectedVersion,
        int? actualVersion,
        string details)
    {
        var versionText = actualVersion.HasValue
            ? $"recorded version {actualVersion.Value}"
            : "no recorded version";

        return
            $"LiteBus PostgreSQL {component} schema drift detected for {schemaName}.{tableName}. " +
            $"Expected schema version {expectedVersion}, but found {versionText}. {details}";
    }
}
