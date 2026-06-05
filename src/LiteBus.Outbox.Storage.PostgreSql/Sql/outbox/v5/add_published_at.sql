ALTER TABLE {{QualifiedTableName}}
    ADD COLUMN IF NOT EXISTS published_at timestamptz NULL;
