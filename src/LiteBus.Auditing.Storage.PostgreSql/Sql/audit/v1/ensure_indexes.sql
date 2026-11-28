CREATE INDEX IF NOT EXISTS {{RecordsUnlinkedIndexName}}
    ON {{QualifiedRecordsTableName}} (segment, staged_order)
    WHERE sequence = 0;

CREATE INDEX IF NOT EXISTS {{RecordsTenantIndexName}}
    ON {{QualifiedRecordsTableName}} (tenant_id)
    WHERE tenant_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS {{HeadsTenantIndexName}}
    ON {{QualifiedHeadsTableName}} (tenant_id)
    WHERE tenant_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS {{HeadsOpenedIndexName}}
    ON {{QualifiedHeadsTableName}} (opened_at);

CREATE INDEX IF NOT EXISTS {{CheckpointsSegmentIndexName}}
    ON {{QualifiedCheckpointsTableName}} (segment, sequence);

CREATE INDEX IF NOT EXISTS {{CheckpointsUnpublishedIndexName}}
    ON {{QualifiedCheckpointsTableName}} (publish_state, created_at)
    WHERE publish_state = 0;
