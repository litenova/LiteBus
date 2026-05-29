ALTER TABLE {{QualifiedTableName}}
    ADD COLUMN IF NOT EXISTS trace_context jsonb NULL;
