# Transactional Store Bindings

- **ID**: `storage.transactional.writes`
- **Summary**: Binds inbox and outbox append writes to an existing unit-of-work transaction.

## Interface Methods and Entry Points

| Surface | Methods or APIs |
| --- | --- |
| `ITransactionalInboxStore` | `AddAsync`, `AddBatchAsync` |
| `ITransactionalOutboxStore` | `AddAsync`, `AddBatchAsync` |
| PostgreSQL ambient provider | `IPostgreSqlTransactionProvider.TryGetCurrent(...)` |
| PostgreSQL manual binding | `UseExistingConnection(...)` on the stores and `CreateTransactionalStore(...)` on the store registrations |
| EF Core transactional path | `ITransactionalInbox<TContext>`, `ITransactionalOutbox<TContext>`, save-changes interceptors |
| Marten queued writer | `MartenTransactionalOutbox` (`ITransactionalOutbox`) via `UseMartenTransactionalOutbox()` on the PostgreSQL outbox storage builder |

## SQL Behavior

- PostgreSQL transactional stores execute append SQL on caller-supplied connection and transaction.
- EF transactional path stages envelopes until `SaveChanges`, then writes inside current `DbContext` transaction.
- The Marten queued writer stages the schema-owned outbox insert on the scoped `IDocumentSession` with `QueueSqlCommand`, so documents and outbox rows commit in one `SaveChangesAsync`; the writer resolves duplicates with a committed-row lookup before queueing because the queued insert ends with `ON CONFLICT DO NOTHING`.
- Rollback drops both domain and messaging writes for the same transaction scope.
- Strict transactional batches pre-check known duplicates and throw `IdempotencyConflictException` for conflicting content; the interceptor preserves strict duplicate identifiers for tracker or database rejection instead of collapsing them.
- `UseMartenTransactionalOutbox()` cannot be combined with `EnableAmbientTransactionProvider()`: both register `ITransactionalOutbox`, so module build throws `OutboxPostgreSqlStorageConfigurationException`.

## Concurrency Model

- Transactional append does not change processor concurrency; processors still lease from singleton stores.
- Ambient mode policy:
  - `RequireActiveTransaction`: throws when no active transaction is available.
  - `AllowImmediateCommit`: falls back to singleton auto-commit store.

## Index Interaction

- Transactional writes rely on the same primary and idempotency indexes as auto-commit append.
- Duplicate key conflicts still abort or reject transaction according to provider behavior.

## Observability

- No dedicated transactional-write meter is emitted.
- Successful commit appears as increased pending queue-depth count on next diagnostics poll.
- Rollback produces no queue-depth change because rows were never committed.

## Test Coverage

### `LiteBus.Storage.UnitTests`

- `PostgreSqlTransactionalParticipantTests`: active-transaction requirement and fallback mode checks.
- EF transactional unit tests: staged writes and interceptor pre-commit behavior.
- EF module tests: enforcement options and missing-interceptor configuration failures.

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlInboxTransactionalIntegrationTests`: inbox commit and rollback with domain writes.
- `PostgreSqlOutboxTransactionalIntegrationTests`: outbox commit and rollback with domain writes.
- `PostgreSqlTransactionalWritersIntegrationTests`: ambient provider and manual bind paths, including inbox and outbox in one transaction.
- `MartenQueuedTransactionalOutboxTests`: document and outbox row committing together (`A_Marten_document_and_an_outbox_row_commit_together`), nothing stored without save (`Neither_the_document_nor_the_outbox_row_is_stored_without_save_changes`), one row for a twice-staged message (`Staging_the_same_message_twice_in_one_session_stores_one_row`), `AlreadyEnqueued` for a stored id (`A_message_whose_id_is_already_stored_returns_already_enqueued`), strict conflict throwing (`A_strict_submission_with_a_conflicting_key_throws`), and the ambient-provider combination throwing at composition (`Composing_both_the_ambient_provider_and_the_Marten_writer_throws`).
- EF interceptor integration tests: commit path, rollback path, and duplicate-idempotency abort.

## Concrete Example

A command handler writes a domain order row and enqueues an outbox event on one PostgreSQL transaction. On rollback, neither row exists. On commit, both rows exist.

## Related Docs

- [Transactional Messaging Writes](../../reliable-messaging/transactional-writes.md)
- [Inbox EntityFrameworkCore Storage](../../integrations/inbox-ef-core-storage.md)
- [Outbox EntityFrameworkCore Storage](../../integrations/outbox-ef-core-storage.md)
- [Outbox Marten Storage](../../integrations/outbox-marten-storage.md)
