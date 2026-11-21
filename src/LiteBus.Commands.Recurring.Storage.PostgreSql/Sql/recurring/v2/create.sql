CREATE SCHEMA IF NOT EXISTS {{QuotedSchemaName}};

CREATE TABLE IF NOT EXISTS {{QualifiedOccurrenceTableName}}
(
    occurrence_key text NOT NULL,
    loop_name text NOT NULL,
    tenant_id text NULL,
    scheduled_at timestamptz NOT NULL,
    recorded_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (occurrence_key)
);
