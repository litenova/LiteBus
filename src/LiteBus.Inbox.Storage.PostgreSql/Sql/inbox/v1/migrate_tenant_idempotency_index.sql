-- Manual migration for existing LiteBus inbox deployments created before tenant-scoped idempotency.
-- Run once per environment before or together with the updated ensure_indexes script.
--
-- 1. Normalize NULL tenant identifiers so unscoped rows share one idempotency scope.
UPDATE {{QualifiedTableName}}
SET tenant_id = ''
WHERE tenant_id IS NULL;

-- 2. Replace the legacy single-column idempotency unique index.
DROP INDEX IF EXISTS {{QuotedSchemaName}}.{{IdempotencyIndexName}};

CREATE UNIQUE INDEX IF NOT EXISTS {{IdempotencyIndexName}}
    ON {{QualifiedTableName}} (tenant_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;
