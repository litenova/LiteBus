# PostgreSQL Schema Management

- **ID**: `storage.postgresql.schema-management`
- **Summary**: Defines schema create and validation behavior for PostgreSQL inbox and outbox tables, including version metadata and drift checks.

## Public Surface

| Type | Methods |
| --- | --- |
| `PostgreSqlInboxSchema` | `EnsureAsync`, `ValidateAsync`, `GetCreateScript` |
| `PostgreSqlOutboxSchema` | `EnsureAsync`, `ValidateAsync`, `GetCreateScript` |
| `PostgreSqlInboxSchemaInitializer` and `PostgreSqlOutboxSchemaInitializer` | host startup task path |
| `PostgreSqlInboxSchemaDiagnosticCheck` and `PostgreSqlOutboxSchemaDiagnosticCheck` | runtime schema probe path |

## SQL Behavior

- Creates v1 inbox and outbox tables from embedded SQL resources.
- Creates index set required by each adapter, then validates index presence.
- Writes and reads schema version metadata via `litebus_schema_versions`.
- `GetCreateScript` renders final SQL with configured schema, table, and index names.

## Concurrency and Drift Rules

- `EnsureAsync` acquires advisory lock before DDL to avoid duplicate bootstrap races.
- `ValidateAsync` compares table existence, inferred column version, and required index names.
- Missing table, column, or index raises `PostgreSqlSchemaDriftException`.

## Index Expectations

- Inbox: idempotency unique index plus lease index.
- Outbox: idempotency unique index, lease index, and topic index.
- Both tables include insert trigger and notify function in v1 create script.

## Observability

- Diagnostic probe keys:
  - `inbox.postgresql.schema`
  - `outbox.postgresql.schema`
- Schema checks report expected and recorded version through diagnostics store `GetSchemaInfoAsync`.
- Queue depth gauges are independent from schema bootstrap state.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxSchemaTests` and `PostgreSqlOutboxSchemaTests`: idempotent create, concurrent bootstrap, and validation failures.
- `PostgreSqlSchemaScriptTests`: create script content includes v1 objects and indexes.
- `PostgreSqlSchemaDriftTests`: missing column and missing index detection paths.
- `PostgreSqlSchemaHostingTests`: startup initializer behavior for enabled, disabled, and validate-only modes.

### `LiteBus.Storage.UnitTests`

- `PostgreSqlSchemaInspectorTests`: column/version inference used by validation.
- `PostgreSqlTableReferenceTests`: option-to-table mapping used for rendered DDL.

## Related Docs

- [PostgreSQL Schema Management](../../integrations/postgresql-schema-management.md)
- [Architecture](../../architecture/README.md#storage-axis)
