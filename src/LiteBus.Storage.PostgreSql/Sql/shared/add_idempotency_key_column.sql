ALTER TABLE {{QualifiedTableName}}
    ADD COLUMN IF NOT EXISTS idempotency_key text NULL;

CREATE UNIQUE INDEX IF NOT EXISTS {{IdempotencyIndexName}}
    ON {{QualifiedTableName}} (idempotency_key)
    WHERE idempotency_key IS NOT NULL;
