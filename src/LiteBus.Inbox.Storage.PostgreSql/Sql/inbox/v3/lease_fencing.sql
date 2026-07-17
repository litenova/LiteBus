ALTER TABLE {{QualifiedTableName}}
    ADD COLUMN IF NOT EXISTS lease_generation bigint NOT NULL DEFAULT 0;
