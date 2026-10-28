CREATE SCHEMA IF NOT EXISTS {{QuotedSchemaName}};

CREATE TABLE IF NOT EXISTS {{QualifiedTableName}}
(
    idempotency_key text NOT NULL,
    payload text NULL,
    fingerprint text NULL,
    is_completed boolean NOT NULL DEFAULT false,
    claimed_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    claim_token text NULL,
    PRIMARY KEY (idempotency_key)
);
