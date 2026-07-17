SELECT column_name, data_type
FROM information_schema.columns
WHERE table_schema = @schemaName
  AND table_name = @tableName;
