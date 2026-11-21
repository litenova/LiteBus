# Commands Marten Adapter

- **ID**: `storage.commands.adapter.marten`
- **Package**: `LiteBus.Commands.Storage.Marten` (feature bridge)
- **Shared dependency**: none. Marten is referenced directly and the application registers it with `AddMarten`
- **Summary**: A scoped `MartenUnitOfWork` commits the handler session, a singleton `MartenIdempotencyStore` implements command idempotency claims on Marten documents, a scoped `MartenAuditTrail` writes audit records as Marten documents with an optional hash chain, and a singleton `MartenAuditChain` verifies, checkpoints, and expires chained segments. The command module registers the unit of work, the idempotency store, and the audit chain.

## Interface Methods

| Interface | Methods used in adapter |
| --- | --- |
| `IUnitOfWork` | `CommitAsync` |
| `IIdempotencyStore` | `TryClaimAsync`, `CompleteAsync`, `ReleaseAsync` |
| `IAuditTrail` | `WriteAsync` |
| `IAuditChain` | `VerifyAsync`, `CutCheckpointAsync`, `ExpireSegmentAsync` |
| `ICheckpointSource` | `GetLatestAsync` |

## Marten Behavior

- Unit of work: `CommitAsync` saves the scoped `IDocumentSession`. The completion handler behind `UseMartenUnitOfWork` calls it only when the mediation completed successfully, so any other outcome disposes the session without saving.
- Idempotency documents: each `MartenIdempotencyRecord` is keyed by the scoped idempotency key and carries the fingerprint, the claim token that fences the current claim, the completion flag and payload, and the claim and completion timestamps.
- Claim path: each call opens a short serializable session from `IDocumentStore` and commits it before returning, so a granted claim is visible to a concurrent claimant immediately.
- A new key reports `Granted` with a generated claim token. An applied key reports `AlreadyCompleted` with the stored payload when the fingerprint matches or either fingerprint is null, and `FingerprintMismatch` when both are non-null and differ. An unsettled key within `ClaimTimeout` reports `InFlight`.
- When an unsettled key is older than `ClaimTimeout`, the new claimant takes it over with a new token, replacing the previous fingerprint and timestamp. `ClaimTimeout` is five minutes by default, must be positive, and is measured on the messaging `TimeProvider`.
- Completion marks the key applied with the recorded payload and `CompletedAt`, but only while the document still carries the caller's token. A mismatched or missing document is a no-op and stores nothing.
- Release deletes only unsettled claims still carrying the caller's token, so a release racing a completion cannot reopen an applied key and a late release cannot free a taken-over claim.
- Audit documents: the store writes one `MartenAuditRecord` per mediated message, a flattened copy of `AuditRecord` with the actor unfolded into scalar columns.
- A success audit record is staged on the scoped session and commits with the handler change in the same `SaveChangesAsync`. A denial, failure, cancellation, or invalid record is written out of band on a short session that saves immediately.

## Hash Chain

- `MartenAuditTrailOptions.UseHashChain(Action<MartenAuditHashChainBuilder>?)` enables sequencing and chaining per segment. Without it the trail writes plain records with no sequence numbers and no chain. `UseMartenAuditChain` registers the options instance, `IAuditChain`, and `ICheckpointSource` as a command child module, applying the chain defaults when the configuration action leaves the chain off; building without the chain throws `LiteBusConfigurationException`.
- The builder configures each piece or keeps its default: `StoreAs<TDocument>` (default `MartenAuditRecord` through `MartenAuditRecordMapper`), `SegmentBy` with a segmenter, a delegate, or a registry-resolved type (default category and year through `MartenAuditRecordSegmenter`, with no genesis record), `UseScheme` with the write scheme, `AcceptScheme` for each older scheme history still carries, `CheckpointWhen` (default every 100 records), `SignCheckpointsWith`, and `PublishCheckpointsTo`. A piece that may need services is given as an instance or as a type; schemes and the policy are instances only.
- The default scheme is `sha256-v1` (`MartenAuditHashSchemes.Sha256V1`): `MartenAuditRecordCanonicalizer`, `PreviousHashLinkRule`, and `Sha256AuditHasher`. The canonical form is the same field set the chain always hashed, and `MartenAuditHashChain.ComputeHash` delegates to the canonicalizer, so records written before schemes verify unchanged.
- A custom document starts with `StoreAs`: every other typed call must name the same document type, or the builder throws `InvalidOperationException`. Two different schemes sharing one name fail the same way, as does a custom document without a mapper, segmenter, or write scheme.
- One `MartenAuditSegmentHead` per segment carries the last sequence and hash, the document count, the last checkpoint sequence and time, the scheme the last record used, and whether the segment has expired. The document identity is the segment name. Appending to an expired segment throws `InvalidOperationException`, as does a segmenter returning a blank segment.
- On the first append to a new segment, the segmenter's `CreateGenesis` result, when non-null, is stored at sequence one and the record follows at two. A segment that opens without a genesis record starts its first record at one with a null previous hash.
- Every cut checkpoint is stored as a `MartenAuditCheckpoint` document and then published, so `MartenAuditCheckpointSource` answers verification without an external store. Cutting a checkpoint of an empty or expired segment throws `InvalidOperationException`. A publisher that throws fails the operation it was called from.
- `MartenAuditTrail.VerifyChainAsync(segment)` returns `AuditChainVerification`: every break with its sequence and reason, the head, and the comparison against the latest stored checkpoint with its signature validity. This replaces the preview.1 first-broken-sequence return.
- `ExpireSegmentAsync` publishes a `Final` checkpoint, deletes every document of the segment, and marks the head expired, so the remaining segments still verify. Expiring an empty segment throws `InvalidOperationException`; a second call returns the stored final checkpoint instead of cutting another one, so a retention job stays safe to retry.
- Time-based checkpoints need a trigger without new writes: `MartenAuditCheckpointBackgroundService` asks the policy for each open segment on `MartenAuditTrailOptions.CheckpointScanInterval` (five minutes by default, must be positive) and cuts where it says due. The module registers the loop only when the policy reports `IsTimeBased`. One segment whose checkpoint fails never skips the rest, and a failed scan retries on the next interval.

## Concurrency Model

- Claim atomicity comes from the document identity plus serializable sessions. Two concurrent claims of one key contend on one row, and the storage engine refuses the loser with a unique violation (`23505`), a serialization failure (`40001`), or a deadlock (`40P01`). The store retries each refusal against the committed winner.
- Concurrent claims never both grant. The refused caller is answered `InFlight` once the winner commits.
- The claim token fences settlement. A completion or release that arrives after a takeover is a no-op, so the superseded claimant cannot corrupt the new claimant. `ClaimTimeout` stays above the longest claiming mediation so a live claimant settles first.
- Calls run on the store's own sessions, so the claim is not atomic with the handler's change. A crash between the two leaves an unsettled claim that blocks the key until it expires and is taken over.
- Chained appends contend on the segment head under Marten optimistic concurrency. Two writers that load the same head serialize on one row: the loser fails its commit and retries against the winner, so two records never share a sequence number. A head creation race fails with a unique violation on the head identity and retries the same way.

## Ordering

- The audit completion handler runs at `HandlerPriorities.Observability` and the unit-of-work commit at `HandlerPriorities.UnitOfWork`, so the success record is always staged before the session commits. A chained record is instead written out of band on its own serialized session, even for a success, because the sequence number is assigned under the segment lock at commit time; it is durable on its own rather than atomic with the handler change.
- The chain asks the checkpoint policy after each append commits, and a cut covers the committed head. A concurrent append that moves the head between the load and the commit retries the cut from the newer head, because an explicit cut means the segment as it stands now.
- The query `UseUnitOfWork` hook accepts `MartenUnitOfWork` directly. This package ships no query builder extension so the command storage package does not reference the query axis.

## Observability

- There is no diagnostic probe. Marten owns its schema under its default auto-create setting, and there is no LiteBus schema to validate.

## Test Coverage

### `LiteBus.Storage.IntegrationTests` (`PostgreSql/Marten/`)

- `MartenIdempotencyStoreTests` covers claim, replay with the stored payload, fingerprint mismatch, null-fingerprint compatibility on both sides, in-flight answers regardless of fingerprint, takeover after the timeout with fingerprint replacement, late settle after takeover leaving the new claimant alone, completion on a missing key storing nothing, release freeing only unsettled claims, concurrent claims granting exactly one, concurrent takeovers granting exactly one, and option validation.
- In `MartenUnitOfWorkTests`, the handler document commits on success, a guard denial commits nothing staged, and a handler failure commits nothing staged.
- In `MartenAuditTrailTests`, a success record commits with the handler change, a staged record disappears with a rolled-back session, a failure record persists out of band while the staged change rolls back, and both paths end to end through an audited mediation.
- In `MartenHashChainAuditTrailTests`, chained writes assign increasing sequences with linked hashes and the scheme name, a checkpoint is published every interval, an intact chain verifies intact, a tampered record reports a hash mismatch, a missing record reports a gap, a chained success persists without the scoped commit, default segments group by category and year, appending to an expired segment throws, and records written before schemes verify unchanged.
- In `MartenAuditChainTests`, an explicit cut stores, signs, and publishes a checkpoint, cutting a segment without records throws, expiry publishes a final checkpoint and removes only that segment, expiry is idempotent, a custom document chain round-trips, a genesis record opens a new segment, a scheme change keeps history verifiable, signed checkpoints detect a forged signature, and a missing covered record reports a count mismatch.
- In `MartenAuditChainModuleTests`, building with a time-based policy registers the checkpoint loop, building with a record policy skips it, building without the chain throws, and `UseMartenAuditChain` registers the module with defaults.
- In `MartenAuditChainBackgroundServiceTests`, a time-based policy cuts checkpoints without new writes, and the service without a time-based policy returns immediately.

## Related Docs

- [Marten Unit of Work, Idempotency, and Audit](../../integrations/commands-marten.md)
- [In-Process Idempotency](../../concepts/idempotency.md)
- [Auditing](../../concepts/auditing.md)
