SELECT version
FROM {{QualifiedMetadataTableName}}
WHERE component = @component
  AND schema_name = @schemaName
  AND table_name = @tableName;
