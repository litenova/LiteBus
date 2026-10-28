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

- Creates inbox tables at schema version 1 and outbox tables at schema version 2 from embedded SQL resources.
- Creates index set required by each adapter, then validates index presence.
- Writes and reads schema version metadata via `litebus_schema_versions`.
- `GetCreateScript` renders final SQL with configured schema, table, and index names.

## Schema Upgrades

- `EnsureAsync` upgrades an outbox table found at version 1 to version 2 automatically by running the ordered `UpgradeScripts` from the component schema definition; the version 2 upgrade adds the nullable `handler` column and preserves every row.
- Fresh databases are created at the current version directly, with no upgrade step.
- `ValidateAsync` never mutates: on a version 1 outbox table it raises `PostgreSqlSchemaDriftException` with details naming the available upgrade path.
- Components without upgrades (inbox, idempotency, recurring lease, all at version 1) report the version gap only.

## Concurrency and Drift Rules

- `EnsureAsync` acquires advisory lock before DDL to avoid duplicate bootstrap races.
- `ValidateAsync` compares table existence, inferred column version, and required index names.
- Missing table, column, or index raises `PostgreSqlSchemaDriftException`.

## Index Expectations

- Inbox: idempotency unique index plus lease index.
- Outbox: idempotency unique index, lease index, and topic index (unchanged by the version 2 upgrade).
- Both tables include insert trigger and notify function in the create script.

## Observability

- Diagnostic probe keys:
  - `inbox.postgresql.schema`
  - `outbox.postgresql.schema`
- Schema checks report expected and recorded version through diagnostics store `GetSchemaInfoAsync`.
- Queue depth gauges are independent from schema bootstrap state.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxSchemaTests` and `PostgreSqlOutboxSchemaTests`: idempotent create, concurrent bootstrap, and validation failures.
- `PostgreSqlOutboxSchemaUpgradeTests`: automatic version 1 to version 2 upgrade with row preservation, handler read-back after upgrade, drift naming the upgrade path, and direct version 2 creation on a fresh database.
- `PostgreSqlSchemaScriptTests`: create script content includes current-version objects and indexes.
- `PostgreSqlSchemaDriftTests`: missing column and missing index detection paths.
- `PostgreSqlSchemaHostingTests`: startup initializer behavior for enabled, disabled, and validate-only modes.

### `LiteBus.Storage.UnitTests`

- `PostgreSqlSchemaInspectorTests`: column/version inference used by validation.
- `PostgreSqlTableReferenceTests`: option-to-table mapping used for rendered DDL.

## Related Docs

- [PostgreSQL Schema Management](../../integrations/postgresql-schema-management.md)
- [Architecture](../../architecture/README.md#storage-axis)
