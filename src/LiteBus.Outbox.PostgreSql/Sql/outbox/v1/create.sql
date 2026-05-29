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
    tenant_id text NULL
);

CREATE INDEX IF NOT EXISTS {{LeaseIndexName}}
    ON {{QualifiedTableName}} (status, visible_after, lease_expires_at, created_at);

CREATE INDEX IF NOT EXISTS {{TopicIndexName}}
    ON {{QualifiedTableName}} (topic)
    WHERE topic IS NOT NULL;
