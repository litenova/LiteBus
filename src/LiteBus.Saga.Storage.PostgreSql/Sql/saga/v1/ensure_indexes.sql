CREATE INDEX IF NOT EXISTS {{CompletedIndexName}}
    ON {{QualifiedTableName}} (is_completed, updated_at);
