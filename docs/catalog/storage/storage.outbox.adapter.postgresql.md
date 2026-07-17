# Outbox PostgreSQL Adapter

- **ID**: `storage.outbox.adapter.postgresql`
- **Package**: `LiteBus.Outbox.Storage.PostgreSql` (feature bridge)
- **Shared dependency**: `LiteBus.Storage.PostgreSql` (technology adapter)
- **Summary**: A single `PostgreSqlOutboxStore` instance implements outbox append, processing, and operations store roles on PostgreSQL.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `IOutboxStore` | `AddAsync`, `AddBatchAsync` |
| `IOutboxLeaseStore` | `LeasePendingAsync`, `RenewLeaseAsync` (via `ILeaseRenewable`) |
| `IOutboxStateWriter` | `PersistAsync` |
| `IOutboxDeadLetterStore` | `RequeueAsync` |
| `IOutboxRetentionStore` | `DeletePublishedOlderThanAsync` |
| `IOutboxDiagnosticsStore` | `GetStatusCountsAsync`, `GetSchemaInfoAsync` |
| `IOutboxMessageQuery` and `IOutboxPurgeStore` | `QueryAsync`, `PurgeAsync` |
| `ITransactionalOutboxStore` | `AddAsync`, `AddBatchAsync` on existing Npgsql transaction |

## SQL Behavior

- Table: configurable, defaults to `public.litebus_outbox_messages`.
- Insert path uses `ON CONFLICT DO NOTHING RETURNING`; returned rows report `Enqueued`, while conflict lookups report `AlreadyEnqueued`.
- Retention delete uses `COALESCE(published_at, created_at) < @older_than`.
- Dead-letter replay resets row to pending and clears lease and error fields.
- Status diagnostics uses grouped counts by outbox status.

## Concurrency Model

- Leasing uses `FOR UPDATE SKIP LOCKED`.
- Store records `lease_owner` and `lease_expires_at` when rows are claimed.
- Persist updates are lease-guarded; stale workers report skipped/rejected outcomes.
- Expired publishing leases are eligible for a new worker.
- Payload is stored as text so configured encryptors can return opaque ciphertext. Existing tables created with a JSONB payload column require an application migration before encrypted writes use the text parameter type.

## Indexes

Schema v1 creates and validates:

- Unique idempotency index: `(tenant_id, idempotency_key)` filtered on non-null key.
- Lease scan index: `(status, visible_after, lease_expires_at, created_at)`.
- Topic index: `(topic)` with filter `topic IS NOT NULL`.
- Primary key: `message_id`.
- Optional insert trigger and function for `NOTIFY`.

## Observability

- Queue depth gauge: `litebus.outbox.queue.depth` with tag `litebus.outbox.status`.
- Processor instruments from leased and persisted outcomes (`leases_acquired`, `dispatch_duration`, `published`, `failed`, `dead_lettered`, `persist_skipped`, `persist_rejected`).
- Cleanup error counter: `litebus.outbox.cleanup.errors`.
- Schema probe: `outbox.postgresql.schema`.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlOutboxStoreTests` (contract inheritance): append, lease, persist, replay, retention, diagnostics.
- `PostgreSqlOutboxEndToEndTests`: publish success, retry, and dead-letter flows.
- `PostgreSqlOutboxWorkSignalNotifyTests` and `PostgreSqlOutboxWorkSignalTests`: NOTIFY wake and reconnect.
- `PostgreSqlOutboxTransactionalIntegrationTests`: atomic commit and rollback with domain writes.
- `PostgreSqlModuleRegistrationTests`: all outbox role interfaces resolve from one singleton.

### `LiteBus.Storage.UnitTests`

- `PostgreSqlTransactionalParticipantTests`: transactional mode policy behavior.
- `PostgreSqlSchemaInspectorTests`: schema and index validation utilities.

## Related Docs

- [PostgreSQL Schema Management](../../integrations/postgresql-schema-management.md)
- [Transactional Messaging Writes](../../reliable-messaging/transactional-writes.md)
- [Reliable Messaging](../../reliable-messaging/README.md)
