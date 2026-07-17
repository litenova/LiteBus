# Inbox PostgreSQL Adapter

- **ID**: `storage.inbox.adapter.postgresql`
- **Package**: `LiteBus.Inbox.Storage.PostgreSql` (layer 4)
- **Shared dependency**: `LiteBus.Storage.PostgreSql` (layer 3)
- **Summary**: A single `PostgreSqlInboxStore` instance implements inbox append, processing, and operations store roles on PostgreSQL.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `IInboxStore` | `AddAsync`, `AddBatchAsync` |
| `IInboxLeaseStore` | `LeasePendingAsync`, `RenewLeaseAsync` (via `ILeaseRenewable`) |
| `IInboxStateWriter` | `PersistAsync` |
| `IInboxDeadLetterStore` | `RequeueAsync` |
| `IInboxRetentionStore` | `DeleteCompletedOlderThanAsync` |
| `IInboxDiagnosticsStore` | `GetStatusCountsAsync`, `GetSchemaInfoAsync` |
| `IInboxMessageQuery` and `IInboxPurgeStore` | `QueryAsync`, `PurgeAsync` |
| `ITransactionalInboxStore` | `AddAsync`, `AddBatchAsync` on existing Npgsql transaction |

## SQL Behavior

- Table: configurable, defaults to `public.litebus_inbox_messages`.
- Insert path uses `ON CONFLICT DO NOTHING`, then fetches existing row for idempotent duplicates.
- Retention delete uses `COALESCE(completed_at, created_at) < @older_than`.
- Dead-letter replay uses `UPDATE ... SET status = pending, visible_after = NULL, attempt_count = 0`.
- Status diagnostics uses grouped count query: `SELECT status, COUNT(*) FROM ... GROUP BY status`.

## Concurrency Model

- Leasing uses `FOR UPDATE SKIP LOCKED` to claim disjoint rows across workers.
- Lease ownership is recorded with `lease_owner` and `lease_expires_at`.
- State writes are lease-guarded; stale workers return skipped/rejected persist counts.
- Expired leases are reclaimable, which gives at-least-once execution behavior.

## Indexes

Schema v1 creates and validates:

- Unique idempotency index: `(tenant_id, idempotency_key)` with filter `idempotency_key IS NOT NULL`.
- Lease scan index: `(status, visible_after, lease_expires_at, created_at)`.
- Primary key: `message_id`.
- Optional insert trigger and function for `NOTIFY` wake-ups.

## Observability

- Queue depth gauge: `litebus.inbox.queue.depth` with tag `litebus.inbox.status`.
- Processor counters and histograms are fed by lease and state transitions (`leases_acquired`, `dispatch_duration`, `succeeded`, `failed`, `dead_lettered`, `persist_skipped`, `persist_rejected`).
- Cleanup error counter: `litebus.inbox.cleanup.errors`.
- Schema probe: `inbox.postgresql.schema` validates columns and indexes.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxStoreTests` (contract inheritance): append, lease, persist, replay, retention, diagnostics.
- `PostgreSqlInboxEndToEndTests`: full processor pass and retry/dead-letter transitions.
- `PostgreSqlInboxProcessorLeaseStressTests`: parallel workers with one terminal result per message.
- `PostgreSqlInboxWorkSignalNotifyTests` and `PostgreSqlInboxWorkSignalTests`: LISTEN/NOTIFY wake and reconnect.
- `PostgreSqlInboxTransactionalIntegrationTests`: commit and rollback behavior with shared transaction.
- `PostgreSqlModuleRegistrationTests`: confirms all inbox role interfaces resolve from one singleton.

### `LiteBus.Storage.UnitTests`

- `PostgreSqlTransactionalParticipantTests`: active-transaction requirement and fallback policy.
- `PostgreSqlSchemaInspectorTests` and `PostgreSqlTableReferenceTests`: schema/version/index helper behavior used by diagnostics.

## Related Docs

- [PostgreSQL Schema Management](../../integrations/postgresql-schema-management.md)
- [Transactional Messaging Writes](../../reliable-messaging/transactional-writes.md)
- [Reliable Messaging](../../reliable-messaging/README.md)
