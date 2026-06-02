-- Version 3: message-neutral inbox storage (renames v6 preview command_id and default table name).
DO $litebus_inbox_v3$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = '{{UnquotedSchemaName}}'
          AND table_name = '{{UnquotedTableName}}'
          AND column_name = 'command_id')
    THEN
        ALTER TABLE {{QualifiedTableName}} RENAME COLUMN command_id TO message_id;
    END IF;
END
$litebus_inbox_v3$;

DO $litebus_inbox_v3_table$
BEGIN
    IF '{{UnquotedTableName}}' = 'litebus_inbox_messages'
       AND NOT EXISTS (
           SELECT 1
           FROM information_schema.tables
           WHERE table_schema = '{{UnquotedSchemaName}}'
             AND table_name = 'litebus_inbox_messages')
       AND EXISTS (
           SELECT 1
           FROM information_schema.tables
           WHERE table_schema = '{{UnquotedSchemaName}}'
             AND table_name = 'litebus_inbox_commands')
    THEN
        ALTER TABLE {{LegacyQualifiedTableName}} RENAME TO {{QuotedTableName}};
    END IF;
END
$litebus_inbox_v3_table$;
