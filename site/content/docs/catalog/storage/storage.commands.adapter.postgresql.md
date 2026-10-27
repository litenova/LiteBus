# Commands PostgreSQL Adapter

- **ID**: `storage.commands.adapter.postgresql`
- **Package**: `LiteBus.Commands.Storage.PostgreSql` (feature bridge)
- **Shared dependency**: `LiteBus.Storage.PostgreSql` (technology adapter)
- **Summary**: A single `PostgreSqlIdempotencyStore` instance implements command idempotency claims on PostgreSQL, registered as a child module of the command module.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `IIdempotencyStore` | `TryClaimAsync`, `CompleteAsync`, `ReleaseAsync` |

## SQL Behavior

- Table: configurable, defaults to `public.litebus_idempotency_keys`.
- Claim path uses `INSERT ... ON CONFLICT DO NOTHING`; an inserted row reports `Granted`, while a refused insert reads the winner.
- A winner marked applied reports `AlreadyCompleted` with the stored payload, or `FingerprintMismatch` when the stored and incoming fingerprints are both set and differ; a winner still unsettled reports `InFlight`.
- A winner that vanished between the refused insert and the read was released concurrently, so the claim loops and inserts again.
- Completion upserts the key as applied with the recorded payload and `completed_at`.
- Release deletes only unsettled rows, so a release racing a completion cannot reopen an applied key.
- Timestamps are stamped by the database (`DEFAULT now()`), so the store takes no clock dependency.

## Concurrency Model

- Claim atomicity comes from the primary key on `idempotency_key`: two concurrent claims of one key insert one row and refuse the other.
- An in-flight claim answers `InFlight` rather than granting, like the in-memory test double.
- Concurrent claims never both grant; the refused caller retries and is then granted or answered once the first mediation settles.
- Calls run on the store's own connections, so the claim is not atomic with the handler's change; a crash between the two leaves an unsettled row that is taken over once its claim outlasts `ClaimTimeout`.

## Indexes

Schema v1 creates and validates:

- Primary key: `idempotency_key`.
- Retention index: `(completed_at)` with filter `is_completed = true`, so a retention purge has a cutoff.

## Observability

- Schema probe: `idempotency.postgresql.schema` validates columns and indexes.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/`)

- `PostgreSqlIdempotencyStoreTests`: claim/complete/replay, release-then-reclaim with applied keys never released, concurrent claims granting exactly one, in-flight answers, fingerprint mismatch and replay, and timeout takeover, schema ensure idempotency and concurrent bootstrap, drift on a missing table, the schema probe healthy and unhealthy, one end-to-end mediation replaying the stored result without rerunning the handler, and schema creation through the module initializer.

## Related Docs

- [PostgreSQL Idempotency Store](../../integrations/commands-postgresql-idempotency-store.md)
- [PostgreSQL Schema Management](../../integrations/postgresql-schema-management.md)
- [In-Process Idempotency](../../concepts/idempotency.md)
