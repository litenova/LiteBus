# Transactional Store Bindings

- **ID**: `storage.transactional.writes`
- **Summary**: Binds inbox and outbox append writes to an existing unit-of-work transaction.

## Interface Methods and Entry Points

| Surface | Methods or APIs |
| --- | --- |
| `ITransactionalInboxStore` | `AddAsync`, `AddBatchAsync` |
| `ITransactionalOutboxStore` | `AddAsync`, `AddBatchAsync` |
| PostgreSQL ambient provider | `IPostgreSqlTransactionProvider.TryGetCurrent(...)` |
| PostgreSQL manual binding | `UseExistingConnection(...)` / `CreateTransactionalStore(...)` style APIs on stores |
| EF Core transactional path | `ITransactionalInbox<TContext>`, `ITransactionalOutbox<TContext>`, save-changes interceptors |

## SQL Behavior

- PostgreSQL transactional stores execute append SQL on caller-supplied connection and transaction.
- EF transactional path stages envelopes until `SaveChanges`, then writes inside current `DbContext` transaction.
- Rollback drops both domain and messaging writes for the same transaction scope.

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
- EF interceptor integration tests: commit path, rollback path, and duplicate-idempotency abort.

## Concrete Example

A command handler writes a domain order row and enqueues an outbox event on one PostgreSQL transaction. On rollback, neither row exists. On commit, both rows exist.

## Related Docs

- [Transactional Messaging Writes](../../reliable-messaging/transactional-writes.md)
- [Inbox EntityFrameworkCore Storage](../../integrations/inbox-ef-core-storage.md)
- [Outbox EntityFrameworkCore Storage](../../integrations/outbox-ef-core-storage.md)
