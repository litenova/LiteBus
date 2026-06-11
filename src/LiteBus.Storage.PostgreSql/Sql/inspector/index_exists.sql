SELECT EXISTS (SELECT 1
               FROM pg_indexes
               WHERE schemaname = @schemaName
                 AND tablename = @tableName
                 AND indexname = @indexName);
