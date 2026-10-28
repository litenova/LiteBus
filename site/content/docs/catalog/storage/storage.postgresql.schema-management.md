# PostgreSQL Schema Management

- **ID**: `storage.postgresql.schema-management`
- **Summary**: Defines how the PostgreSQL inbox and outbox tables are created and validated, including version metadata and drift checks.

## Public Surface

| Type | Methods |
| --- | --- |
| `PostgreSqlInboxSchema` | `EnsureAsync`, `ValidateAsync`, `GetCreateScript` |
| `PostgreSqlOutboxSchema` | `EnsureAsync`, `ValidateAsync`, `GetCreateScript` |
| `PostgreSqlInboxSchemaInitializer` and `PostgreSqlOutboxSchemaInitializer` | host startup task path |
| `PostgreSqlInboxSchemaDiagnosticCheck` and `PostgreSqlOutboxSchemaDiagnosticCheck` | runtime schema probe path |

## SQL Behavior

- The initializer creates inbox tables at schema version 1 and outbox tables at schema version 2 from embedded SQL resources.
- It creates the index set each adapter requires, then validates that the indexes exist.
- It writes and reads schema version metadata through `litebus_schema_versions`.
- `GetCreateScript` renders the final SQL with the configured schema, table, and index names.

## Schema Upgrades

- `EnsureAsync` upgrades an outbox table found at version 1 to version 2 automatically by running the ordered `UpgradeScripts` from the component schema definition. The version 2 upgrade adds the nullable `handler` column and preserves every row.
- Fresh databases are created at the current version directly, with no upgrade step.
- `ValidateAsync` never mutates. On a version 1 outbox table it raises `PostgreSqlSchemaDriftException` with details that name the available upgrade path.
- Components without upgrades (inbox, idempotency, recurring lease, all at version 1) report the version gap only.

## Concurrency and Drift Rules

- `EnsureAsync` acquires an advisory lock before DDL, so concurrent bootstraps do not run it twice.
- `ValidateAsync` compares table existence, inferred column version, and required index names.
- A missing table, column, or index raises `PostgreSqlSchemaDriftException`.

## Index Expectations

- The inbox table has an idempotency unique index and a lease index.
- The outbox table has an idempotency unique index, a lease index, and a topic index. The version 2 upgrade leaves them unchanged.
- Both create scripts include an insert trigger and a notify function.

## Observability

- Diagnostic probe keys:
  - `inbox.postgresql.schema`
  - `outbox.postgresql.schema`
- Schema checks report the expected and recorded versions through the diagnostics store `GetSchemaInfoAsync`.
- Queue depth gauges do not depend on schema bootstrap state.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxSchemaTests` and `PostgreSqlOutboxSchemaTests` cover idempotent create, concurrent bootstrap, and validation failures.
- `PostgreSqlOutboxSchemaUpgradeTests` covers automatic version 1 to version 2 upgrade with row preservation, handler read-back after upgrade, drift naming the upgrade path, and direct version 2 creation on a fresh database.
- `PostgreSqlSchemaScriptTests` verifies that create script content includes current-version objects and indexes.
- `PostgreSqlSchemaDriftTests` covers missing column and missing index detection.
- `PostgreSqlSchemaHostingTests` covers startup initializer behavior for enabled, disabled, and validate-only modes.

### `LiteBus.Storage.UnitTests`

- `PostgreSqlSchemaInspectorTests` covers the column and version inference used by validation.
- `PostgreSqlTableReferenceTests` covers the option-to-table mapping used for rendered DDL.

## Related Docs

- [PostgreSQL Schema Management](../../integrations/postgresql-schema-management.md)
- [Architecture](../../architecture/README.md#storage-axis)
