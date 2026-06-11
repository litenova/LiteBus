CREATE SCHEMA IF NOT EXISTS {{QuotedSchemaName}};

CREATE TABLE IF NOT EXISTS {{QualifiedTableName}}
(
    correlation_id
    text
    NOT
    NULL,
    saga_type
    text
    NOT
    NULL,
    state_json
    jsonb
    NOT
    NULL,
    optimistic_lock_version
    integer
    NOT
    NULL,
    is_completed
    boolean
    NOT
    NULL,
    created_at
    timestamptz
    NOT
    NULL,
    updated_at
    timestamptz
    NOT
    NULL,
    PRIMARY
    KEY
(
    correlation_id,
    saga_type
)
    );

CREATE INDEX IF NOT EXISTS {{CompletedIndexName}}
    ON {{QualifiedTableName}} (is_completed, updated_at);
