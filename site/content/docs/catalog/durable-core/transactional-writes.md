# Transactional Inbox/Outbox Writes

**ID:** `durable-core.transactional-writes`
**Maturity:** GA

## Summary

Transactional inbox and outbox writes persist domain state and envelopes in one database transaction, so they commit or roll back together.

## What It Does

Transactional writers insert envelope rows inside a transaction the application already opened. LiteBus does not open or commit domain transactions.

EF Core stages envelopes on a shared `DbContext` until `SaveChangesAsync`. PostgreSQL supports ambient `IPostgreSqlTransactionProvider` or manual `CreateTransactionalStore` binding. Marten applications whose session also serves reads use the queued writer from [Outbox Marten Storage](../../integrations/outbox-marten-storage.md), which stages the schema-owned insert on the current `IDocumentSession`.

Background processors always use non-transactional singleton stores on their own pooled connections.

## Public Surface

### Consumer Contracts

- **`IInbox` / `IOutbox`**: These are immediate-commit singleton writers for ingress, tools, and standalone paths. Do not use them inside an open domain transaction.
- **`ITransactionalInbox` / `ITransactionalOutbox`**: These are PostgreSQL scoped writers that participate in an ambient transaction through `IPostgreSqlTransactionProvider`.
- **`ITransactionalInbox<TContext>` / `ITransactionalOutbox<TContext>`**: These are EF Core scoped writers bound to a shared `DbContext`. Envelopes stay staged until `SaveChangesAsync`.
- **`StoreBoundTransactionalInbox` / `StoreBoundTransactionalOutbox`**: These provide a manual PostgreSQL bind for when the application explicitly owns the connection and transaction lifecycle.
- **Marten queued writer**: This is an `ITransactionalOutbox` backed by the scoped Marten `IDocumentSession`. Staged rows commit with `SaveChangesAsync`.

### Invocation

- **Immediate commit** calls `IInbox.AcceptAsync` / `IOutbox.EnqueueAsync` on the default singleton store.
- **EF shared context** resolves `ITransactionalOutbox<TContext>` (or the inbox equivalent) in the same scope as the domain `DbContext`. Enqueue or accept during handler work, then call `SaveChangesAsync` once at the unit of work boundary.
- **PostgreSQL ambient**: With an active `NpgsqlTransaction`, resolve the transactional writer. The store append uses the provider's current connection.
- **Manual bind**: `CreateTransactionalStore(...)` returns a store instance tied to the caller's open transaction. Pass it to the store-bound transactional writer.

### Registration

- **EF Core**: Call `UseEntityFrameworkCoreStorage<TContext>()` on the inbox or outbox builder, plus **`EnableSaveChangesInterceptor()`** for automatic flush on SaveChanges.
- **PostgreSQL ambient**: Call **`EnableAmbientTransactionProvider()`** on the PostgreSQL storage builder. Register a shared `NpgsqlDataSource` across application and LiteBus storage.
- **Manual bind**: The application calls the storage factory **`CreateTransactionalStore`** at request scope. No ambient provider is required.
- **Marten queued writer**: Call **`UseMartenTransactionalOutbox()`** on the PostgreSQL outbox storage builder. The application registers Marten with `AddMarten` against the same database.

### Configuration

- **`TransactionalWriteMode.RequireActiveTransaction`**: This mode throws when no ambient transaction is active on PostgreSQL transactional participants.
- **EF interceptor registration**: This registration is required for deferred persist until SaveChanges on the outbox and inbox EF paths.
- **Shared data source**: Use one `NpgsqlDataSource` per database when combining inbox, outbox, and application repositories on PostgreSQL.

### Extension Points

- **`IPostgreSqlTransactionProvider`**: The application or the storage package implements this to expose the ambient transaction (PostgreSQL axis).
- **EF `SaveChangesInterceptor`**: The LiteBus-provided interceptor stages envelope entities on the shared context.
- Custom store adapters implement append against a caller-supplied connection and transaction when extending the storage axis.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Storage.EntityFrameworkCore` | EF transactional inbox |
| `LiteBus.Outbox.Storage.EntityFrameworkCore` | EF transactional outbox |
| `LiteBus.Inbox.Storage.PostgreSql` | PostgreSQL transactional inbox |
| `LiteBus.Outbox.Storage.PostgreSql` | PostgreSQL transactional outbox |
| `LiteBus.Outbox.Storage.Marten` | Marten queued transactional outbox |
| `LiteBus.Storage.PostgreSql` | `IPostgreSqlTransactionProvider` |

## Requires

- Share one `NpgsqlDataSource` per database across inbox, outbox, and application when using PostgreSQL.
- Have an active transaction or a shared `DbContext` in place before transactional enqueue or accept.
- Register processors separately. They do not reuse request connections.

## Invariants

- `IInbox` / `IOutbox` inside an open domain transaction commit separately (anti-pattern).
- On the transactional EF path, a duplicate `message_id` or `(tenant_id, idempotency_key)` fails `SaveChanges` and rolls back the entire unit of work. There is no silent idempotent accept.
- `TransactionalWriteMode.RequireActiveTransaction` throws when no ambient transaction is active.
- In-memory storage has no cross-store transaction support.

## Non-Goals

- This capability does not join broker ingress to domain transactions at the edge.
- This capability does not let processors participate in application request transactions.
- This capability does not provide dedicated SQL Server transactional stores. The EF Core adapter supports the SQL Server provider, and a dedicated adapter is planned (see [Roadmap](../../roadmap/README.md)).

## Observability

There is no dedicated transactional-write meter. Success and failure appear through application persistence signals and queue depth after commit.

### Transactional Write Failures (EF Core)

- **Kind**: Exception with application log (no LiteBus-specific counter)
- **When emitted**: `SaveChangesAsync` throws on constraint violation, duplicate idempotency key in EF strict path, or context disposal before save
- **Tags/dimensions**: N/A
- **How to enable**: Standard EF Core and host logging, with optional OpenTelemetry EF instrumentation from the application.
- **Operational note**: Duplicate idempotency in the EF transactional path aborts the entire unit of work. Alert on a rising `DbUpdateException` rate at the commit boundary.

### Transactional Write Failures (PostgreSQL)

- **Kind**: `NpgsqlException` / PostgreSQL constraint error (no LiteBus-specific counter)
- **When emitted**: Append violates a unique constraint, the provider is inactive with `RequireActiveTransaction`, or the transaction rolled back before commit
- **Tags/dimensions**: N/A
- **How to enable**: Application database logging and PostgreSQL exporter
- **Operational note**: `InboxParticipant_should_throw_when_provider_inactive` and outbox equivalent confirm that misconfiguration surfaces as actionable exceptions at call time.

### `litebus.inbox.queue.depth` / `litebus.outbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.QueueDepthInstrumentName` / `LiteBusOutboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: After commit only. Staged-but-uncommitted envelopes do not appear.
- **Tags/dimensions**: `litebus.inbox.status` / `litebus.outbox.status`
- **How to enable**: `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()`
- **Operational note**: Depth increases only after successful SaveChanges or PostgreSQL commit. Rollback leaves depth unchanged, confirming the atomic domain and messaging write.

## References

- [Transactional messaging writes](../../reliable-messaging/transactional-writes.md)
- [Inbox EF Core storage](../../integrations/inbox-ef-core-storage.md)
- [Outbox EF Core storage](../../integrations/outbox-ef-core-storage.md)
- [Outbox Marten storage](../../integrations/outbox-marten-storage.md)

## Test Coverage

### Covered Use Cases

#### `PostgreSqlInboxTransactionalIntegrationTests.CreateTransactionalStore_ShouldRollbackDomainAndInboxTogether`

- **Use case**: PostgreSQL inbox rolls back with the domain
- **Test kind**: Integration
- **Description**: A shared transaction abort on PostgreSQL
- **Behavior**: A domain write with an inbox accept, then a rollback
- **Expected outcome**: No inbox row is committed
- **Remarks**:

#### `PostgreSqlInboxTransactionalIntegrationTests.CreateTransactionalStore_ShouldCommitDomainAndInboxTogether`

- **Use case**: PostgreSQL inbox commits with the domain
- **Test kind**: Integration
- **Description**: A shared transaction commit on PostgreSQL
- **Behavior**: A domain write with an inbox accept, then a commit
- **Expected outcome**: Both are persisted
- **Remarks**:

#### `PostgreSqlOutboxTransactionalIntegrationTests.UseExistingConnection_ShouldRollbackDomainAndOutboxTogether`

- **Use case**: PostgreSQL outbox rolls back with the domain
- **Test kind**: Integration
- **Description**: An ambient connection with an aborted transaction
- **Behavior**: A domain write with an outbox enqueue, then a rollback
- **Expected outcome**: The outbox row is rolled back
- **Remarks**:

#### `PostgreSqlOutboxTransactionalIntegrationTests.UseExistingConnection_ShouldCommitDomainAndOutboxTogether`

- **Use case**: PostgreSQL outbox commits with the domain
- **Test kind**: Integration
- **Description**: An ambient connection with a committed transaction
- **Behavior**: A domain write with an outbox enqueue, then a commit
- **Expected outcome**: Both are persisted
- **Remarks**:

#### `PostgreSqlTransactionalWritersIntegrationTests.ManualBind_inbox_and_outbox_should_roll_back_together`

- **Use case**: Manual bind of inbox and outbox in the same transaction
- **Test kind**: Integration
- **Description**: A dual manual bind on one PostgreSQL transaction
- **Behavior**: Accept and enqueue on bound stores, then a rollback
- **Expected outcome**: Both commit, or neither does
- **Remarks**:

#### `PostgreSqlTransactionalWritersIntegrationTests.AmbientProvider_should_enqueue_outbox_in_active_transaction`

- **Use case**: Outbox enqueue through the ambient provider
- **Test kind**: Integration
- **Description**: The `EnableAmbientTransactionProvider` path
- **Behavior**: Enqueue inside an active ambient transaction
- **Expected outcome**: The envelope stays in the open transaction until commit
- **Remarks**:

#### `PostgreSqlTransactionalWritersIntegrationTests.TransactionalOutbox_manual_bind_should_commit_with_domain`

- **Use case**: Manual bind outbox commits with the domain
- **Test kind**: Integration
- **Description**: A `CreateTransactionalStore` manual bind
- **Behavior**: A domain write with an outbox enqueue, then a commit
- **Expected outcome**: Both are committed together
- **Remarks**:

#### `EfCoreOutboxTransactionalIntegrationTests.UseExistingDbContext_ShouldRollbackDomainAndOutboxTogether`

- **Use case**: EF outbox rolls back on SaveChanges abort
- **Test kind**: Integration
- **Description**: A shared DbContext with a failed SaveChanges
- **Behavior**: A domain entity and a staged outbox envelope, then a transaction abort
- **Expected outcome**: The rollback clears the outbox row
- **Remarks**:

#### `EfCoreOutboxTransactionalIntegrationTests.UseExistingDbContext_ShouldCommitDomainAndOutboxTogether`

- **Use case**: EF outbox commits on SaveChanges
- **Test kind**: Integration
- **Description**: A shared DbContext with a single SaveChanges
- **Behavior**: A domain entity and a staged outbox envelope, then a commit
- **Expected outcome**: Both are committed
- **Remarks**:

#### `EfCoreInboxSaveChangesInterceptorRollbackTests.SaveChangesAsync_ShouldNotPersistInboxMessage_WhenTransactionRollsBack`

- **Use case**: An EF interceptor rollback does not persist the inbox message
- **Test kind**: Integration
- **Description**: An EF inbox interceptor with a transaction abort
- **Behavior**: Accept is staged on the context, and SaveChanges rolls back
- **Expected outcome**: No inbox row is stored
- **Remarks**:

#### `EfCoreInboxSaveChangesInterceptorAbortTests.SaveChangesAsync_WhenDuplicateIdempotencyKeyConflicts_ShouldAbortTransaction`

- **Use case**: EF duplicate idempotency aborts the unit of work (inbox)
- **Test kind**: Integration
- **Description**: A duplicate idempotency key on the EF inbox path
- **Behavior**: A second accept in the same SaveChanges batch
- **Expected outcome**: The whole transaction fails
- **Remarks**:

#### `EfCoreOutboxSaveChangesInterceptorAbortTests.SaveChangesAsync_WhenDuplicateIdempotencyKeyConflicts_ShouldAbortTransaction`

- **Use case**: EF duplicate idempotency aborts the unit of work (outbox)
- **Test kind**: Integration
- **Description**: A duplicate idempotency key on the EF outbox path
- **Behavior**: A second enqueue in the same SaveChanges batch
- **Expected outcome**: The whole transaction fails
- **Remarks**:

#### `PostgreSqlTransactionalParticipantTests.InboxParticipant_should_throw_when_provider_inactive_and_require_active`

- **Use case**: The require-active-transaction mode throws for inbox
- **Test kind**: Unit
- **Description**: A PostgreSQL inbox participant without an active provider
- **Behavior**: Accept when `RequireActiveTransaction` is set and the provider is inactive
- **Expected outcome**: The call throws an actionable exception
- **Remarks**:

#### `PostgreSqlTransactionalParticipantTests.OutboxParticipant_should_throw_when_provider_inactive_and_require_active`

- **Use case**: The require-active-transaction mode throws for outbox
- **Test kind**: Unit
- **Description**: A PostgreSQL outbox participant without an active provider
- **Behavior**: Enqueue when `RequireActiveTransaction` is set and the provider is inactive
- **Expected outcome**: The call throws an actionable exception
- **Remarks**:

#### `StoreBoundTransactionalInboxTests.AcceptAsync_should_write_to_bound_store_only`

- **Use case**: Store-bound transactional inbox
- **Test kind**: Unit
- **Description**: A manual PostgreSQL bind for inbox
- **Behavior**: Accept on a bound store instance
- **Expected outcome**: Writes go only to the bound store
- **Remarks**:

#### `StoreBoundTransactionalOutboxTests.EnqueueAsync_should_write_to_bound_store_only`

- **Use case**: Store-bound transactional outbox
- **Test kind**: Unit
- **Description**: A manual bind for outbox
- **Behavior**: Enqueue on a bound store instance
- **Expected outcome**: The write stays isolated to the bound store instance
- **Remarks**:

#### `EfCoreOutboxTransactionalUnitTests.UseExistingDbContext_defers_persistence_until_save_changes`

- **Use case**: EF defers persist until SaveChanges
- **Test kind**: Unit
- **Description**: Staged envelopes on a shared DbContext
- **Behavior**: An enqueue before SaveChanges
- **Expected outcome**: The envelope is not visible until save
- **Remarks**:

#### `LiteBusOutboxSaveChangesInterceptorUnitTests.SavingChangesAsync_when_same_context_accumulates_parallel_enqueues_should_persist_all_envelopes`

- **Use case**: Parallel EF enqueues on the same context
- **Test kind**: Unit
- **Description**: Concurrent enqueues on one DbContext
- **Behavior**: Parallel `EnqueueAsync` calls before a single SaveChanges
- **Expected outcome**: All rows are stored on commit
- **Remarks**:

#### `MartenQueuedTransactionalOutboxTests.A_Marten_document_and_an_outbox_row_commit_together`

- **Use case**: Marten queued writer commits with documents
- **Test kind**: Integration
- **Description**: `UseMartenTransactionalOutbox` with a scoped Marten session
- **Behavior**: A document write with an outbox enqueue, then `SaveChangesAsync`
- **Expected outcome**: Both are committed in one save
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

#### `MartenQueuedTransactionalOutboxTests.Neither_the_document_nor_the_outbox_row_is_stored_without_save_changes`

- **Use case**: Marten queued writer rolls back with documents
- **Test kind**: Integration
- **Description**: A staged document and envelope without save
- **Behavior**: The session is disposed before `SaveChangesAsync`
- **Expected outcome**: Neither row is stored
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

#### `MartenQueuedTransactionalOutboxTests.Composing_both_the_ambient_provider_and_the_Marten_writer_throws`

- **Use case**: Marten writer rejects the ambient provider combination
- **Test kind**: Integration
- **Description**: `EnableAmbientTransactionProvider` with `UseMartenTransactionalOutbox` on one builder
- **Behavior**: A module build with both transactional routes
- **Expected outcome**: The build throws a configuration exception
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Ingress joined to domain transaction | No (by design) | Not supported | N/A | N/A |
| SQL Server transactional stores | Partial | The EF Core adapter works with the SQL Server provider, but a dedicated adapter is not available | N/A | N/A |

### Out-of-Scope Use Cases

- Processor participation in request-scoped transactions
- Broker ingress auto-commit joined to the domain unit of work
- In-memory cross-store transactions
