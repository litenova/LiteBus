namespace LiteBus.Storage.PostgreSql;

/// <summary>
///     Embedded SQL resource paths used by the shared PostgreSQL schema loader.
/// </summary>
internal static class PostgreSqlSchemaEmbeddedSql
{
    /// <summary>
    ///     Embedded resource path for metadata table creation SQL.
    /// </summary>
    internal const string MetadataCreate = "metadata/create.sql";

    /// <summary>
    ///     Embedded resource path for metadata version lookup SQL.
    /// </summary>
    internal const string MetadataSelectVersion = "metadata/select_version.sql";

    /// <summary>
    ///     Embedded resource path for metadata version upsert SQL.
    /// </summary>
    internal const string MetadataUpsertVersion = "metadata/upsert_version.sql";

    /// <summary>
    ///     Embedded resource path for table existence inspection SQL.
    /// </summary>
    internal const string InspectorTableExists = "inspector/table_exists.sql";

    /// <summary>
    ///     Embedded resource path for column listing inspection SQL.
    /// </summary>
    internal const string InspectorListColumns = "inspector/list_columns.sql";

    /// <summary>
    ///     Embedded resource path for index existence inspection SQL.
    /// </summary>
    internal const string InspectorIndexExists = "inspector/index_exists.sql";
}
