# Outbox PostgreSQL Adapter

- **ID**: `storage.outbox.adapter.postgresql`
- **Package**: `LiteBus.Outbox.Storage.PostgreSql` (feature bridge)
- **Shared dependency**: `LiteBus.Storage.PostgreSql` (technology adapter)
- **Summary**: A single `PostgreSqlOutboxStore` instance implements outbox append, processing, and operations store roles on PostgreSQL.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `IOutboxStore` | `AddAsync`, `AddBatchAsync` |
| `IOutboxLeaseStore` | `LeasePendingAsync`, `RenewLeaseAsync`, `ReleaseLeaseAsync` (via `ILeaseRenewable`) |
| `IOutboxStateWriter` | `PersistAsync` |
| `IOutboxDeadLetterStore` | `RequeueAsync` |
| `IOutboxRetentionStore` | `DeletePublishedOlderThanAsync` |
| `IOutboxDiagnosticsStore` | `GetStatusCountsAsync`, `GetSchemaInfoAsync` |
| `IOutboxMessageQuery` and `IOutboxPurgeStore` | `QueryAsync`, `PurgeAsync` |
| `ITransactionalOutboxStore` | `AddAsync`, `AddBatchAsync` on existing Npgsql transaction |

## SQL Behavior

- The table is configurable and defaults to `public.litebus_outbox_messages`.
- The insert path uses `ON CONFLICT DO NOTHING RETURNING`. Returned rows report `Enqueued`, while conflict lookups report `AlreadyEnqueued`.
- Batch inserts run in 1,000-row statements inside the caller transaction, so large batches stay under the PostgreSQL parameter cap.
- Retention deletes use `COALESCE(published_at, created_at) < @older_than`.
- Dead-letter replay resets the row to pending and clears the lease, error, and failure-timestamp fields (`lease_owner`, `lease_expires_at`, `last_error`, `error_type`, `first_failed_at`, `dead_lettered_at`).
- Status diagnostics use grouped counts by outbox status.
- Strict identical replay compares tenants through normalized values, so unscoped null and empty-string tenants match and return `AlreadyEnqueued`.
- A null filter or lease tenant means no tenant restriction. An empty string matches only unscoped rows.
- Rows addressed to one handler by per-handler delivery carry the handler full name in the nullable `handler` column. Broadcast rows leave it null.

## Schema Versions

- The outbox schema uses version 2, which adds the nullable `handler` text column to the version 1 shape. The index set is unchanged.
- `EnsureAsync` upgrades a version 1 table automatically with `ALTER TABLE {{QualifiedTableName}} ADD COLUMN IF NOT EXISTS handler text NULL`, then records version 2. A fresh database is created at version 2 directly.
- `ValidateAsync` on a version 1 table raises `PostgreSqlSchemaDriftException` with details naming the available upgrade path instead of only reporting the version gap.
- Migration-owned deployments copy the version 2 create script for new installs or run the version 2 upgrade script against version 1 tables. See `SqlFiles` on `PostgreSqlOutboxSchema`.

## Insert Statement for Other Units of Work

`PostgreSqlOutboxSchema.CreateInsertStatement(envelope, options)` builds a `PostgreSqlOutboxInsertStatement` that carries the parameterized insert for one `OutboxEnvelope` against `CurrentSchemaVersion`. `Sql` uses Npgsql positional parameters `$1` through `$19` with an explicit `$18::jsonb` cast for the trace context, and `Values` holds the converted values in the same order with `DBNull.Value` for every absent value. The statement covers all 19 insert columns, ends with `ON CONFLICT DO NOTHING`, and has no `RETURNING` clause.

`FormatSql(placeholder)` returns the same statement with each `$n` marker replaced by `placeholder` while keeping casts. It serves units of work that take their own placeholder character. Marten sessions execute it through `IDocumentOperations.QueueSqlCommand('?', statement.FormatSql('?'), [.. statement.Values])`, which replaces each `?` with the next positional parameter inside the batch `SaveChangesAsync` sends.

The statement silently skips conflicting inserts, so idempotency resolution (Strict and ReturnExisting) is the caller's job. Look up the existing row before queueing rather than reading it back from a skipped insert. `LiteBus.Outbox.Storage.Marten` is the shipped caller. See [Outbox Marten Storage](../../integrations/outbox-marten-storage.md) and [storage.outbox.adapter.marten](storage.outbox.adapter.marten.md). Callers must not edit the statement text, because a schema change updates `CreateInsertStatement` in one place.

## Concurrency Model

- Leasing uses `FOR UPDATE SKIP LOCKED`.
- The store records `lease_owner` and `lease_expires_at` when rows are claimed.
- Persist updates check the lease. A worker that lost its lease reports a skipped or rejected outcome.
- A new worker can claim expired publishing leases.
- Payload is stored as text so configured encryptors can return opaque ciphertext. Existing tables created with a JSONB payload column require an application migration before encrypted writes use the text parameter type.

## Indexes

Schema v2 creates and validates the following (version 1 tables gain the `handler` column through the automatic upgrade, and the index set is unchanged):

- The unique idempotency index covers `(tenant_id, idempotency_key)` and applies only to non-null keys.
- The lease scan index covers `(status, visible_after, lease_expires_at, created_at)`.
- The topic index covers `(topic)` with the filter `topic IS NOT NULL`.
- The primary key is `message_id`.
- An optional insert trigger and function send `NOTIFY`.

## Observability

- The queue depth gauge is `litebus.outbox.queue.depth`, tagged with `litebus.outbox.status`.
- Processor instruments record leased and persisted outcomes (`leases_acquired`, `dispatch_duration`, `published`, `failed`, `dead_lettered`, `persist_skipped`, `persist_rejected`).
- The cleanup error counter is `litebus.outbox.cleanup.errors`.
- The schema probe is `outbox.postgresql.schema`.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlOutboxStoreTests` inherits the contract suite and covers append, lease, persist, replay, retention, and diagnostics.
- `PostgreSqlOutboxEndToEndTests` covers publish success, retry, and dead-letter flows.
- `PostgreSqlOutboxSchemaUpgradeTests` covers version 1 upgrade to version 2 with row preservation (`EnsureAsync_WhenVersion1TableExists_ShouldUpgradeToVersion2AndPreserveRows`), handler read-back after upgrade (`LeasePendingAsync_AfterVersion1Upgrade_ShouldReturnEnvelopeHandler`), drift naming the upgrade path when initialization is disabled (`RunAsync_WhenInitializationIsDisabled_ShouldThrowDriftNamingUpgradePath`), and direct version 2 creation on a fresh database (`EnsureAsync_WhenDatabaseIsFresh_ShouldCreateVersion2Directly`).
- `PostgreSqlOutboxWorkSignalNotifyTests` and `PostgreSqlOutboxWorkSignalTests` cover NOTIFY wake and reconnect.
- `PostgreSqlOutboxTransactionalIntegrationTests` covers atomic commit and rollback with domain writes.
- `PostgreSqlModuleRegistrationTests` verifies that all outbox role interfaces resolve from one singleton.

### `LiteBus.Storage.UnitTests`

- `PostgreSqlTransactionalParticipantTests` covers transactional mode policy behavior.
- `PostgreSqlSchemaInspectorTests` covers the schema and index validation utilities.

## Related Docs

- [PostgreSQL Schema Management](../../integrations/postgresql-schema-management.md)
- [Outbox Marten Storage](../../integrations/outbox-marten-storage.md)
- [Transactional Messaging Writes](../../reliable-messaging/transactional-writes.md)
- [Reliable Messaging](../../reliable-messaging/README.md)
