ALTER TABLE {{QualifiedTableName}}
    ALTER COLUMN payload TYPE text
    USING payload::text;
