SELECT column_name
FROM information_schema.columns
WHERE table_schema = @schemaName
  AND table_name = @tableName;
