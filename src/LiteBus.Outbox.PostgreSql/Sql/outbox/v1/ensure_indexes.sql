CREATE INDEX IF NOT EXISTS {{LeaseIndexName}}
    ON {{QualifiedTableName}} (status, visible_after, lease_expires_at, created_at);

CREATE INDEX IF NOT EXISTS {{TopicIndexName}}
    ON {{QualifiedTableName}} (topic)
    WHERE topic IS NOT NULL;
