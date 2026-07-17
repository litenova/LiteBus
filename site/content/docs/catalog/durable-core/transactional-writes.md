# Transactional Inbox/Outbox Writes

**ID:** `durable-core.transactional-writes`
**Maturity:** GA

## Summary

Persist domain state and inbox/outbox envelopes in one database transaction so they commit or roll back together.

## What It Does

Transactional writers insert envelope rows inside a transaction the application already opened. LiteBus does not open or commit domain transactions. EF Core stages envelopes on a shared `DbContext` until `SaveChangesAsync`. PostgreSQL supports ambient `IPostgreSqlTransactionProvider` or manual `CreateTransactionalStore` binding. Background processors always use non-transactional singleton stores on their own pooled connections.

## Public Surface

### Consumer Contracts

- **`IInbox` / `IOutbox`**: Immediate-commit singleton writers for ingress, tools, and standalone paths. Do not use inside an open domain transaction.
- **`ITransactionalInbox` / `ITransactionalOutbox`**: PostgreSQL scoped writers that participate in an ambient transaction via `IPostgreSqlTransactionProvider`.
- **`ITransactionalInbox<TContext>` / `ITransactionalOutbox<TContext>`**: EF Core scoped writers bound to a shared `DbContext`; envelopes stage until `SaveChangesAsync`.
- **`StoreBoundTransactionalInbox` / `StoreBoundTransactionalOutbox`**: Manual PostgreSQL bind when the application owns connection and transaction lifecycle explicitly.

### Invocation

- **Immediate commit**: `IInbox.AcceptAsync` / `IOutbox.EnqueueAsync` on the default singleton store.
- **EF shared context**: Resolve `ITransactionalOutbox<TContext>` (or inbox equivalent) in the same scope as the domain `DbContext`, enqueue/accept during handler work, then call `SaveChangesAsync` once at the UoW boundary.
- **PostgreSQL ambient**: With an active `NpgsqlTransaction`, resolve transactional writer; store append uses the provider's current connection.
- **Manual bind**: `CreateTransactionalStore(...)` returns a store instance tied to the caller's open transaction; pass to store-bound transactional writer.

### Registration

- **EF Core**: `UseEntityFrameworkCoreStorage<TContext>()` on inbox/outbox builder plus **`EnableSaveChangesInterceptor()`** for automatic flush on SaveChanges.
- **PostgreSQL ambient**: **`EnableAmbientTransactionProvider()`** on the PostgreSQL storage builder; register shared `NpgsqlDataSource` across application and LiteBus storage.
- **Manual bind**: Application calls storage factory **`CreateTransactionalStore`** at request scope; no ambient provider required.

### Configuration

- **`TransactionalWriteMode.RequireActiveTransaction`**: Throws when no ambient transaction is active on PostgreSQL transactional participants.
- **EF interceptor registration**: Required for deferred persist until SaveChanges on outbox/inbox EF paths.
- **Shared data source**: One `NpgsqlDataSource` per database when combining inbox, outbox, and application repositories on PostgreSQL.

### Extension Points

- **`IPostgreSqlTransactionProvider`**: Application or storage package supplies ambient transaction activation (PostgreSQL axis).
- **EF `SaveChangesInterceptor`**: LiteBus-provided interceptor stages envelope entities on the shared context.
- Custom store adapters implement append against caller-supplied connection/transaction when extending storage axis.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Storage.EntityFrameworkCore` | EF transactional inbox |
| `LiteBus.Outbox.Storage.EntityFrameworkCore` | EF transactional outbox |
| `LiteBus.Inbox.Storage.PostgreSql` | PostgreSQL transactional inbox |
| `LiteBus.Outbox.Storage.PostgreSql` | PostgreSQL transactional outbox |
| `LiteBus.Storage.PostgreSql` | `IPostgreSqlTransactionProvider` |

## Requires

- One `NpgsqlDataSource` per database shared across inbox, outbox, and application when using PostgreSQL
- Active transaction or shared `DbContext` before transactional enqueue/accept
- Processors registered separately; they do not reuse request connections

## Invariants

- `IInbox` / `IOutbox` inside an open domain transaction commits separately (anti-pattern)
- Transactional EF path: duplicate `message_id` or `(tenant_id, idempotency_key)` fails `SaveChanges` and rolls back entire UoW (no silent idempotent accept)
- `TransactionalWriteMode.RequireActiveTransaction` throws when no ambient transaction is active
- In-memory storage has no cross-store transaction support

## Non-Goals

- Joining broker ingress to domain transactions at the edge
- Processor participation in application request transactions
- SQL Server transactional stores (planned)

## Observability

No dedicated transactional-write meter. Success and failure appear through application persistence signals and queue depth after commit.

### Transactional Write Failures (EF Core)

- **Kind**: Exception + application log (no LiteBus-specific counter)
- **When emitted**: `SaveChangesAsync` throws on constraint violation, duplicate idempotency key in EF strict path, or context disposal before save
- **Tags/dimensions**: N/A
- **How to enable**: Standard EF Core and host logging; optional OpenTelemetry EF instrumentation from application
- **Operational note**: Duplicate idempotency in EF transactional path aborts the entire UoW; alert on rising `DbUpdateException` rate at commit boundary

### Transactional Write Failures (PostgreSQL)

- **Kind**: `NpgsqlException` / PostgreSQL constraint error (no LiteBus-specific counter)
- **When emitted**: Append violates unique constraint, provider inactive when `RequireActiveTransaction`, or transaction rolled back before commit
- **Tags/dimensions**: N/A
- **How to enable**: Application database logging and PostgreSQL exporter
- **Operational note**: `InboxParticipant_should_throw_when_provider_inactive` and outbox equivalent confirm misconfiguration surfaces as actionable exceptions at call time

### `litebus.inbox.queue.depth` / `litebus.outbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.QueueDepthInstrumentName` / `LiteBusOutboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: After commit only; staged-but-uncommitted envelopes do not appear
- **Tags/dimensions**: `litebus.inbox.status` / `litebus.outbox.status`
- **How to enable**: `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()`
- **Operational note**: Depth increases only after successful SaveChanges or PostgreSQL commit; rollback leaves depth unchanged, confirming atomic domain + messaging write

## Deep Docs

- [Transactional messaging writes](../../reliable-messaging/transactional-writes.md)
- [Inbox EF Core storage](../../integrations/inbox-ef-core-storage.md)
- [Outbox EF Core storage](../../integrations/outbox-ef-core-storage.md)

## Test Coverage

### Covered Use Cases

#### `PostgreSqlInboxTransactionalIntegrationTests.CreateTransactionalStore_ShouldRollbackDomainAndInboxTogether`

- **Use case**: PostgreSQL inbox rollback with domain
- **Test kind**: Integration
- **Description**: Shared transaction abort on PostgreSQL
- **Behavior**: Domain write plus inbox accept, then rollback
- **Expected outcome**: No inbox row committed
- **Remarks**:

#### `PostgreSqlInboxTransactionalIntegrationTests.CreateTransactionalStore_ShouldCommitDomainAndInboxTogether`

- **Use case**: PostgreSQL inbox commit with domain
- **Test kind**: Integration
- **Description**: Shared transaction commit on PostgreSQL
- **Behavior**: Domain write plus inbox accept, then commit
- **Expected outcome**: Both persisted
- **Remarks**:

#### `PostgreSqlOutboxTransactionalIntegrationTests.UseExistingConnection_ShouldRollbackDomainAndOutboxTogether`

- **Use case**: PostgreSQL outbox rollback with domain
- **Test kind**: Integration
- **Description**: Ambient connection with aborted transaction
- **Behavior**: Domain write plus outbox enqueue, then rollback
- **Expected outcome**: Outbox row rolled back
- **Remarks**:

#### `PostgreSqlOutboxTransactionalIntegrationTests.UseExistingConnection_ShouldCommitDomainAndOutboxTogether`

- **Use case**: PostgreSQL outbox commit with domain
- **Test kind**: Integration
- **Description**: Ambient connection with committed transaction
- **Behavior**: Domain write plus outbox enqueue, then commit
- **Expected outcome**: Both persisted
- **Remarks**:

#### `PostgreSqlTransactionalWritersIntegrationTests.ManualBind_inbox_and_outbox_should_roll_back_together`

- **Use case**: Manual bind inbox and outbox same transaction
- **Test kind**: Integration
- **Description**: Dual manual bind on one PostgreSQL transaction
- **Behavior**: Accept and enqueue on bound stores, then rollback
- **Expected outcome**: Both or neither
- **Remarks**:

#### `PostgreSqlTransactionalWritersIntegrationTests.AmbientProvider_should_enqueue_outbox_in_active_transaction`

- **Use case**: Ambient provider outbox enqueue
- **Test kind**: Integration
- **Description**: `EnableAmbientTransactionProvider` path
- **Behavior**: Enqueue inside active ambient transaction
- **Expected outcome**: Envelope in open transaction until commit
- **Remarks**:

#### `PostgreSqlTransactionalWritersIntegrationTests.TransactionalOutbox_manual_bind_should_commit_with_domain`

- **Use case**: Manual bind outbox commit with domain
- **Test kind**: Integration
- **Description**: `CreateTransactionalStore` manual bind
- **Behavior**: Domain plus outbox enqueue, then commit
- **Expected outcome**: Committed together
- **Remarks**:

#### `EfCoreOutboxTransactionalIntegrationTests.UseExistingDbContext_ShouldRollbackDomainAndOutboxTogether`

- **Use case**: EF outbox rollback on SaveChanges abort
- **Test kind**: Integration
- **Description**: Shared DbContext with failed SaveChanges
- **Behavior**: Domain entity plus staged outbox envelope, transaction abort
- **Expected outcome**: Rollback clears outbox row
- **Remarks**:

#### `EfCoreOutboxTransactionalIntegrationTests.UseExistingDbContext_ShouldCommitDomainAndOutboxTogether`

- **Use case**: EF outbox commit on SaveChanges
- **Test kind**: Integration
- **Description**: Shared DbContext single SaveChanges
- **Behavior**: Domain entity plus staged outbox envelope, commit
- **Expected outcome**: Both committed
- **Remarks**:

#### `EfCoreInboxSaveChangesInterceptorRollbackTests.SaveChangesAsync_ShouldNotPersistInboxMessage_WhenTransactionRollsBack`

- **Use case**: EF interceptor rollback does not persist inbox
- **Test kind**: Integration
- **Description**: EF inbox interceptor with transaction abort
- **Behavior**: Accept staged on context, SaveChanges rolls back
- **Expected outcome**: No inbox row
- **Remarks**:

#### `EfCoreInboxSaveChangesInterceptorAbortTests.SaveChangesAsync_WhenDuplicateIdempotencyKeyConflicts_ShouldAbortTransaction`

- **Use case**: EF duplicate idempotency aborts UoW (inbox)
- **Test kind**: Integration
- **Description**: Duplicate idempotency key on EF inbox path
- **Behavior**: Second accept in same SaveChanges batch
- **Expected outcome**: Whole transaction fails
- **Remarks**:

#### `EfCoreOutboxSaveChangesInterceptorAbortTests.SaveChangesAsync_WhenDuplicateIdempotencyKeyConflicts_ShouldAbortTransaction`

- **Use case**: EF duplicate idempotency aborts UoW (outbox)
- **Test kind**: Integration
- **Description**: Duplicate idempotency key on EF outbox path
- **Behavior**: Second enqueue in same SaveChanges batch
- **Expected outcome**: Whole transaction fails
- **Remarks**:

#### `PostgreSqlTransactionalParticipantTests.InboxParticipant_should_throw_when_provider_inactive_and_require_active`

- **Use case**: Require active transaction throws (inbox)
- **Test kind**: Unit
- **Description**: PostgreSQL inbox participant without active provider
- **Behavior**: Accept when `RequireActiveTransaction` and provider inactive
- **Expected outcome**: Actionable exception
- **Remarks**:

#### `PostgreSqlTransactionalParticipantTests.OutboxParticipant_should_throw_when_provider_inactive_and_require_active`

- **Use case**: Require active transaction throws (outbox)
- **Test kind**: Unit
- **Description**: PostgreSQL outbox participant without active provider
- **Behavior**: Enqueue when `RequireActiveTransaction` and provider inactive
- **Expected outcome**: Actionable exception
- **Remarks**:

#### `StoreBoundTransactionalInboxTests.AcceptAsync_should_write_to_bound_store_only`

- **Use case**: Store-bound transactional inbox
- **Test kind**: Unit
- **Description**: Manual PostgreSQL bind for inbox
- **Behavior**: Accept on bound store instance
- **Expected outcome**: Writes only on bound store
- **Remarks**:

#### `StoreBoundTransactionalOutboxTests.EnqueueAsync_should_write_to_bound_store_only`

- **Use case**: Store-bound transactional outbox
- **Test kind**: Unit
- **Description**: Manual bind for outbox
- **Behavior**: Enqueue on bound store instance
- **Expected outcome**: Isolated store instance
- **Remarks**:

#### `EfCoreOutboxTransactionalUnitTests.UseExistingDbContext_defers_persistence_until_save_changes`

- **Use case**: EF deferred persist until SaveChanges
- **Test kind**: Unit
- **Description**: Staged envelopes on shared DbContext
- **Behavior**: Enqueue before SaveChanges
- **Expected outcome**: Not visible until save
- **Remarks**:

#### `LiteBusOutboxSaveChangesInterceptorUnitTests.SavingChangesAsync_when_same_context_accumulates_parallel_enqueues_should_persist_all_envelopes`

- **Use case**: EF parallel enqueues same context
- **Test kind**: Unit
- **Description**: Concurrent enqueues on one DbContext
- **Behavior**: Parallel `EnqueueAsync` before single SaveChanges
- **Expected outcome**: All rows on commit
- **Remarks**:

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Marten session + transactional outbox | Yes | Pattern documented; no Marten fixture test | Integration | Low |
| Ingress joined to domain transaction | No (by design) | N/A |: |: |
| SQL Server transactional stores | No | Not shipped |: |: |

### Out-of-Scope Use Cases

- Processor participation in request-scoped transactions
- Broker ingress auto-commit joined to domain UoW
- In-memory cross-store transactions
