CREATE SCHEMA IF NOT EXISTS {{QuotedSchemaName}};

CREATE TABLE IF NOT EXISTS {{QualifiedTableName}} (
    message_id uuid PRIMARY KEY,
    contract_name text NOT NULL,
    contract_version integer NOT NULL,
    payload jsonb NOT NULL,
    topic text NULL,
    created_at timestamptz NOT NULL,
    visible_after timestamptz NULL,
    status integer NOT NULL,
    attempt_count integer NOT NULL,
    lease_owner text NULL,
    lease_expires_at timestamptz NULL,
    last_error text NULL,
    correlation_id text NULL,
    causation_id text NULL,
    tenant_id text NULL,
    idempotency_key text NULL,
    trace_context jsonb NULL,
    published_at timestamptz NULL,
    last_attempted_at timestamptz NULL,
    first_failed_at timestamptz NULL,
    dead_lettered_at timestamptz NULL,
    last_lease_owner text NULL,
    error_type text NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS {{IdempotencyIndexName}}
    ON {{QualifiedTableName}} (idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS {{LeaseIndexName}}
    ON {{QualifiedTableName}} (status, visible_after, lease_expires_at, created_at);

CREATE INDEX IF NOT EXISTS {{TopicIndexName}}
    ON {{QualifiedTableName}} (topic)
    WHERE topic IS NOT NULL;

CREATE OR REPLACE FUNCTION {{QuotedSchemaName}}.{{NotifyFunctionName}}()
RETURNS trigger AS $$
BEGIN
    PERFORM pg_notify('{{NotifyChannelName}}', '');
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS {{NotifyTriggerName}} ON {{QualifiedTableName}};

CREATE TRIGGER {{NotifyTriggerName}}
    AFTER INSERT ON {{QualifiedTableName}}
    FOR EACH ROW
    EXECUTE FUNCTION {{QuotedSchemaName}}.{{NotifyFunctionName}}();
