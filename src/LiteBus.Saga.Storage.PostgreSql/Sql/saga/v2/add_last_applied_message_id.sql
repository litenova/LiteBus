ALTER TABLE {{QualifiedTableName}}
    ADD COLUMN IF NOT EXISTS last_applied_message_id uuid NULL;
