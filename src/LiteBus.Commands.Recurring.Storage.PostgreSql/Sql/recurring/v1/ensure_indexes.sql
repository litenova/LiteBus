CREATE INDEX IF NOT EXISTS {{ExpiresIndexName}}
    ON {{QualifiedTableName}} (expires_at);
