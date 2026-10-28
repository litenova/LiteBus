CREATE SCHEMA IF NOT EXISTS {{QuotedSchemaName}};

CREATE TABLE IF NOT EXISTS {{QualifiedTableName}}
(
    lease_name text NOT NULL,
    holder text NOT NULL,
    acquired_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    renewed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (lease_name)
);
