# Commands Marten Adapter

- **ID**: `storage.commands.adapter.marten`
- **Package**: `LiteBus.Commands.Storage.Marten` (feature bridge)
- **Shared dependency**: none; Marten is referenced directly and the application registers it with `AddMarten`
- **Summary**: A scoped `MartenUnitOfWork` commits the handler session, a singleton `MartenIdempotencyStore` implements command idempotency claims on Marten documents, and a scoped `MartenAuditTrail` writes audit records as Marten documents, all registered through the command module.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `IUnitOfWork` | `CommitAsync` |
| `IIdempotencyStore` | `TryClaimAsync`, `CompleteAsync`, `ReleaseAsync` |
| `IAuditTrail` | `WriteAsync` |

## Marten Behavior

- Unit of work: `CommitAsync` saves the scoped `IDocumentSession`; the completion handler behind `UseMartenUnitOfWork` calls it only when the mediation completed successfully, so any other outcome disposes the session without saving.
- Idempotency documents: `MartenIdempotencyRecord`, keyed by the scoped idempotency key, carrying the fingerprint, the claim token fencing the current claim, the completion flag and payload, and the claim and completion timestamps.
- Claim path: each call opens a short serializable session from `IDocumentStore` and commits it before returning, so a granted claim is visible to a concurrent claimant immediately.
- A new key reports `Granted` with a generated claim token; an applied key reports `AlreadyCompleted` with the stored payload when the fingerprint matches or either fingerprint is null, and `FingerprintMismatch` when both are non-null and differ; an unsettled key within `ClaimTimeout` reports `InFlight`.
- An unsettled key older than `ClaimTimeout` (five minutes by default, must be positive, measured on the messaging `TimeProvider`) is taken over: the new claimant is granted with a new token and the previous fingerprint and timestamp are replaced.
- Completion marks the key applied with the recorded payload and `CompletedAt` only while the document still carries the caller's token; a mismatched or missing document is a no-op and stores nothing.
- Release deletes only unsettled claims still carrying the caller's token, so a release racing a completion cannot reopen an applied key and a late release cannot free a taken-over claim.
- Audit documents: `MartenAuditRecord`, one flattened copy of `AuditRecord` per mediated message with the actor unfolded into scalar columns.
- A success audit record is staged on the scoped session and commits with the handler change in the same `SaveChangesAsync`; a denial, failure, cancellation, or invalid record is written out of band on a short session that saves immediately.

## Concurrency Model

- Claim atomicity comes from the document identity plus serializable sessions: two concurrent claims of one key contend on one row, and the storage engine refuses the loser with a unique violation (`23505`), a serialization failure (`40001`), or a deadlock (`40P01`), each retried against the committed winner inside the store.
- Concurrent claims never both grant; the refused caller is answered `InFlight` once the winner commits.
- Settlement is fenced by the claim token: a completion or release that arrives after a takeover is a no-op, so the superseded claimant cannot corrupt the new claimant. `ClaimTimeout` stays above the longest claiming mediation so a live claimant settles first.
- Calls run on the store's own sessions, so the claim is not atomic with the handler's change; a crash between the two leaves an unsettled claim that blocks the key until it expires and is taken over.

## Ordering

- The audit completion handler runs at `HandlerPriorities.Observability` and the unit-of-work commit at `HandlerPriorities.UnitOfWork`, so the success record is always staged before the session commits.
- The query `UseUnitOfWork` hook accepts `MartenUnitOfWork` directly; this package ships no query builder extension so the command storage package does not reference the query axis.

## Observability

- No diagnostic probe: Marten owns its schema under its default auto-create setting, and there is no LiteBus schema to validate.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/Marten/`)

- `MartenIdempotencyStoreTests`: claim, replay with the stored payload, fingerprint mismatch, null-fingerprint compatibility on both sides, in-flight answers regardless of fingerprint, takeover after the timeout with fingerprint replacement, late settle after takeover leaving the new claimant alone, completion on a missing key storing nothing, release freeing only unsettled claims, concurrent claims granting exactly one, concurrent takeovers granting exactly one, and option validation.
- `MartenUnitOfWorkTests`: the handler document commits on success, a guard denial commits nothing staged, and a handler failure commits nothing staged.
- `MartenAuditTrailTests`: a success record commits with the handler change, a staged record disappears with a rolled-back session, a failure record persists out of band while the staged change rolls back, and both paths end to end through an audited mediation.

## Related Docs

- [Marten Unit of Work, Idempotency, and Audit](../../integrations/commands-marten.md)
- [In-Process Idempotency](../../concepts/idempotency.md)
- [Auditing](../../concepts/auditing.md)
