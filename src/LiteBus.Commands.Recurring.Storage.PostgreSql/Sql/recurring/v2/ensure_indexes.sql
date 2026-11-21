CREATE INDEX IF NOT EXISTS {{OccurrenceLookupIndexName}}
    ON {{QualifiedOccurrenceTableName}} (loop_name, tenant_id, scheduled_at DESC);
