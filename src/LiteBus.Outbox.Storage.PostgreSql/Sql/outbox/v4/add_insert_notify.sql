CREATE OR REPLACE FUNCTION {{QuotedSchemaName}}.{{NotifyFunctionName}}()
RETURNS trigger AS $$
BEGIN
    PERFORM pg_notify('{{NotifyChannelName}}', '');
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS {{NotifyTriggerName}} ON {{QualifiedTableName}};

CREATE TRIGGER {{NotifyTriggerName}}
    AFTER INSERT ON {{QualifiedTableName}}
    FOR EACH ROW
    EXECUTE FUNCTION {{QuotedSchemaName}}.{{NotifyFunctionName}}();
