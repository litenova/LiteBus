CREATE SCHEMA IF NOT EXISTS {{QuotedSchemaName}};

CREATE TABLE IF NOT EXISTS {{QualifiedRecordsTableName}}
(
    document_key text NOT NULL,
    segment text NOT NULL,
    sequence bigint NOT NULL DEFAULT 0,
    previous_hash text NULL,
    hash text NULL,
    hash_scheme text NULL,
    tenant_id text NULL,
    document jsonb NOT NULL,
    staged_at timestamptz NOT NULL,
    staged_order bigint GENERATED ALWAYS AS IDENTITY,
    PRIMARY KEY (document_key)
);

CREATE UNIQUE INDEX IF NOT EXISTS {{RecordsSequenceIndexName}}
    ON {{QualifiedRecordsTableName}} (segment, sequence)
    WHERE sequence > 0;

CREATE TABLE IF NOT EXISTS {{QualifiedHeadsTableName}}
(
    segment text NOT NULL,
    tenant_id text NULL,
    attributes jsonb NOT NULL DEFAULT '{}',
    opened_at timestamptz NOT NULL,
    last_sequence bigint NOT NULL DEFAULT 0,
    last_hash text NULL,
    document_count bigint NOT NULL DEFAULT 0,
    last_checkpoint_sequence bigint NULL,
    last_checkpoint_at timestamptz NULL,
    expired boolean NOT NULL DEFAULT false,
    version bigint NOT NULL DEFAULT 0,
    PRIMARY KEY (segment)
);

CREATE TABLE IF NOT EXISTS {{QualifiedCheckpointsTableName}}
(
    id uuid NOT NULL,
    segment text NOT NULL,
    sequence bigint NOT NULL,
    document_count bigint NOT NULL,
    hash text NOT NULL,
    hash_scheme text NOT NULL,
    kind integer NOT NULL,
    created_at timestamptz NOT NULL,
    signature_scheme text NULL,
    signature_value text NULL,
    label text NULL,
    publish_state integer NOT NULL DEFAULT 0,
    publish_attempts integer NOT NULL DEFAULT 0,
    last_publish_error text NULL,
    signatures jsonb NOT NULL DEFAULT '[]',
    PRIMARY KEY (id)
);
