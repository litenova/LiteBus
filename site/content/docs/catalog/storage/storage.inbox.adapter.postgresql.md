# Inbox PostgreSQL Adapter

- **ID**: `storage.inbox.adapter.postgresql`
- **Package**: `LiteBus.Inbox.Storage.PostgreSql` (feature bridge)
- **Shared dependency**: `LiteBus.Storage.PostgreSql` (technology adapter)
- **Summary**: A single `PostgreSqlInboxStore` instance implements inbox append, processing, and operations store roles on PostgreSQL.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `IInboxStore` | `AddAsync`, `AddBatchAsync` |
| `IInboxLeaseStore` | `LeasePendingAsync`, `RenewLeaseAsync`, `ReleaseLeaseAsync` (via `ILeaseRenewable`) |
| `IInboxStateWriter` | `PersistAsync` |
| `IInboxDeadLetterStore` | `RequeueAsync` |
| `IInboxRetentionStore` | `DeleteCompletedOlderThanAsync` |
| `IInboxDiagnosticsStore` | `GetStatusCountsAsync`, `GetSchemaInfoAsync` |
| `IInboxMessageQuery` and `IInboxPurgeStore` | `QueryAsync`, `PurgeAsync` |
| `ITransactionalInboxStore` | `AddAsync`, `AddBatchAsync` on existing Npgsql transaction |

## SQL Behavior

- The table is configurable and defaults to `public.litebus_inbox_messages`.
- The insert path uses `ON CONFLICT DO NOTHING RETURNING`. Returned rows report `Accepted`, while conflict lookups report `AlreadyAccepted`.
- Batch inserts run in 1,000-row statements so large batches never approach the PostgreSQL parameter cap.
- A null tenant filter leaves query, purge, and lease unrestricted. An empty tenant filter matches only unscoped rows. Stored and requested tenants compare through `DurableTenantId.Normalize`.
- Retention delete uses `COALESCE(completed_at, created_at) < @older_than`.
- Dead-letter replay uses `UPDATE ... SET status = pending, visible_after = NULL, attempt_count = 0` and clears lease, error, and failure-timestamp columns (`lease_owner`, `lease_expires_at`, `last_error`, `error_type`, `first_failed_at`, `dead_lettered_at`). Only rows still in dead-letter status are rewritten.
- Status diagnostics runs one grouped count query: `SELECT status, COUNT(*) FROM ... GROUP BY status`.

## Concurrency Model

- Leasing uses `FOR UPDATE SKIP LOCKED` to claim disjoint rows across workers.
- Lease ownership is recorded with `lease_owner` and `lease_expires_at`.
- State writes are lease-guarded. Stale workers return skipped/rejected persist counts.
- Expired leases are reclaimable, which gives at-least-once execution behavior.
- Payload is stored as text so configured encryptors can return opaque ciphertext. Existing tables created with a JSONB payload column require an application migration before encrypted writes use the text parameter type.

## Indexes

Schema v1 creates and validates:

- The unique idempotency index is `(tenant_id, idempotency_key)` with filter `idempotency_key IS NOT NULL`.
- The lease scan index is `(status, visible_after, lease_expires_at, created_at)`.
- The primary key is `message_id`.
- An optional insert trigger and function for `NOTIFY` wake-ups.

## Observability

- The queue depth gauge is `litebus.inbox.queue.depth` with tag `litebus.inbox.status`.
- Processor counters and histograms are fed by lease and state transitions (`leases_acquired`, `dispatch_duration`, `succeeded`, `failed`, `dead_lettered`, `persist_skipped`, `persist_rejected`).
- The cleanup error counter is `litebus.inbox.cleanup.errors`.
- The schema probe `inbox.postgresql.schema` validates columns and indexes.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxStoreTests` (contract inheritance) covers append, lease, persist, replay, retention, and diagnostics.
- In `PostgreSqlInboxBatchInsertTests`, a 4,000-envelope batch is stored in input order across chunked statements.
- `PostgreSqlInboxEndToEndTests` covers a full processor pass and retry/dead-letter transitions.
- `PostgreSqlInboxProcessorLeaseStressTests` covers parallel workers with one terminal result per message.
- `PostgreSqlInboxWorkSignalNotifyTests` and `PostgreSqlInboxWorkSignalTests` cover LISTEN/NOTIFY wake and reconnect.
- `PostgreSqlInboxTransactionalIntegrationTests` covers commit and rollback behavior with a shared transaction.
- In `PostgreSqlModuleRegistrationTests`, all inbox role interfaces resolve from one singleton.

### `LiteBus.Storage.UnitTests`

- `PostgreSqlTransactionalParticipantTests` covers the active-transaction requirement and fallback policy.
- `PostgreSqlSchemaInspectorTests` and `PostgreSqlTableReferenceTests` cover schema/version/index helper behavior used by diagnostics.

## Related Docs

- [PostgreSQL Schema Management](../../integrations/postgresql-schema-management.md)
- [Transactional Messaging Writes](../../reliable-messaging/transactional-writes.md)
- [Reliable Messaging](../../reliable-messaging/README.md)
