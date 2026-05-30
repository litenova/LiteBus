CREATE UNIQUE INDEX IF NOT EXISTS {{IdempotencyIndexName}}
    ON {{QualifiedTableName}} (idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS {{LeaseIndexName}}
    ON {{QualifiedTableName}} (status, visible_after, lease_expires_at, created_at);
