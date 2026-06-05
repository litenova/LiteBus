ALTER TABLE {{QualifiedTableName}}
    ADD COLUMN IF NOT EXISTS completed_at timestamptz NULL;
