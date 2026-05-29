INSERT INTO {{QualifiedMetadataTableName}} (
    component,
    schema_name,
    table_name,
    version,
    applied_at)
VALUES (
    @component,
    @schemaName,
    @tableName,
    @version,
    @appliedAt)
ON CONFLICT (component, schema_name, table_name)
DO UPDATE SET
    version = EXCLUDED.version,
    applied_at = EXCLUDED.applied_at;
