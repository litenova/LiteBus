SELECT EXISTS (
    SELECT 1
    FROM information_schema.tables
    WHERE table_schema = @schemaName
      AND table_name = @tableName
      AND table_type = 'BASE TABLE');
