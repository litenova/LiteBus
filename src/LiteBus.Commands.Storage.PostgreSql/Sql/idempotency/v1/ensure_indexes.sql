CREATE INDEX IF NOT EXISTS {{CompletedIndexName}}
    ON {{QualifiedTableName}} (completed_at) WHERE is_completed = true;
