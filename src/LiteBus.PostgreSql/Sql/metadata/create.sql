CREATE SCHEMA IF NOT EXISTS {{QuotedMetadataSchemaName}};

CREATE TABLE IF NOT EXISTS {{QualifiedMetadataTableName}} (
    component text NOT NULL,
    schema_name text NOT NULL,
    table_name text NOT NULL,
    version integer NOT NULL,
    applied_at timestamptz NOT NULL,
    PRIMARY KEY (component, schema_name, table_name)
);
