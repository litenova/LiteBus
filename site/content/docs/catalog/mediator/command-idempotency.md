# Command Idempotency

- **ID**: `mediator.command-idempotency`
- **Name**: Command idempotency
- **Maturity**: GA
- **Summary**: Claims a declared key per command before the handler runs, so a repeat is answered without running the work again.
- **Guide**: [In-process idempotency](../../concepts/idempotency.md)

## What It Does

A command declares the key that identifies it through `IIdempotencyDefinition<TMessage>`, and `CommandModuleBuilder.EnableIdempotency()` registers two shortcuts and one completion handler that claim the key before the handler runs and settle it after: applied on success, released on anything else, so a transient failure does not turn the retry into a false repeat. The shortcut stage runs after guards and validators, so an unauthorized or malformed command cannot claim a key. Commands declaring nothing are ignored, so one call covers the axis and only declaring commands pay for it.

A claim meets one of four answers from the `IIdempotencyStore`. `Granted` runs the message and `AlreadyCompleted` answers the repeat from the stored record. `InFlight` denies a delivery whose key another delivery still holds, and `FingerprintMismatch` denies a repeat whose fingerprint differs from the stored one; both end the mediation as `Denied`, so `TrySendAsync` returns the denial and `SendAsync` raises `LiteBusMessageDeniedException`. The denial carries a machine-readable code from `IdempotencyRefusalCodes`: `idempotency.in-flight` for a claim still running, `idempotency.fingerprint-mismatch` for a key reused with different content.

## Public Surface

```csharp
public sealed class ApplyPaymentCommandDefinition : IIdempotencyDefinition<ApplyPaymentCommand>
{
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<ApplyPaymentCommand>(command => command.PaymentId.ToString())
            .WithFingerprint<ApplyPaymentCommand>(command => $"{command.Amount}:{command.Currency}");
}

services.AddLiteBus(registry =>
{
    registry.AddMessaging(_ => { });

    registry.AddCommands(builder =>
    {
        builder.RegisterFromAssembly(typeof(ApplyPaymentCommand).Assembly);
        builder.EnableIdempotency();
    });
});
```

| API | Role |
| --- | --- |
| `IIdempotencyDefinition<TMessage>` | Declares the key a repeat is recognized by |
| `IdempotencyDeclaration.KeyedBy<TMessage>(selector)` | Projects the key from the message with a pure selector |
| `IdempotencyDeclaration.KeyedByContext()` | Declares a key a guard supplies through `SuppliedIdempotencyKey` when the key cannot be projected from the message |
| `IdempotencyDeclaration.WithFingerprint<TMessage>(selector)` | Projects a content fingerprint alongside the key, so a reused key with different content is denied rather than replayed |
| `IdempotencyDeclaration.Scope` / `ReplayResult` | Key-space prefix defaulting to the message full type name; whether a repeat replays the serialized result of the first attempt |
| `SuppliedIdempotencyKey` | The context-supplied key a guard hands forward through `IExecutionContext.Data` |
| `IIdempotencyStore` | Remembers applied keys: `TryClaimAsync`, `CompleteAsync(key, claimToken, payload, ct)`, `ReleaseAsync(key, claimToken, ct)` |
| `IdempotencyClaim` / `IdempotencyClaimOutcome` | The store answer: `Granted(claimToken)`, `AlreadyCompleted`, `InFlight`, `FingerprintMismatch` |
| `IdempotencyRefusalCodes` | `idempotency.in-flight` and `idempotency.fingerprint-mismatch` denial codes |
| `CommandModuleBuilder.EnableIdempotency()` | Registers the claiming shortcuts and the settling completion handler at `HandlerPriorities.Persistence` |
| `Idempotency.FromCommandKey(suffix, conflictMode)` | Keys a staged outbox envelope by the in-flight command's claimed key |
| `IdempotencyConfigurationException` | Reports a blank key, a blank explicit scope, a missing context key, or another declaration mistake |
| `IdempotencyStoreDiagnosticCheck` | Probe reporting `litebus.idempotency.store` |

## Packages

- `LiteBus.Messaging.Abstractions` (declaration, store contract, claim types)
- `LiteBus.Commands` (shortcuts, completion handler, `EnableIdempotency`)
- `LiteBus.Commands.Storage.PostgreSql` (`UsePostgreSqlIdempotencyStore`)
- `LiteBus.Commands.Storage.Marten` (`MartenIdempotencyStore`)
- `LiteBus.Testing.Mediation` (`InMemoryIdempotencyStore` test double)
- `LiteBus.DurableMessaging.Abstractions` (`Idempotency.FromCommandKey`)

## Requires

- `mediator.commands`
- `mediator.handler-pipeline`
- `mediator.message-definitions`
- `mediator.handler-priority` (settle at `Persistence`, before the commit at `UnitOfWork`)

## Invariants

- Only the mediation that was granted the key settles it; a mediation that never claimed settles nothing, so it can never delete another delivery's in-flight claim. Every grant carries a claim token fenced into the stored row, and the store completes or releases only while the stored claim still carries it, so a late settle after a takeover is a no-op and completing a key the caller does not hold stores nothing.
- A `Succeeded` mediation marks the key applied, with the serialized result when `ReplayResult` is set; an `Answered` mediation releases a key this mediation claimed; `Denied` and `Invalid` settle nothing; `Failed` and `Canceled` release the key so the retry runs.
- A repeat of a result-producing command without `ReplayResult` raises `PipelineContractException` naming the fix rather than inventing a default answer. A grant without a token and any unrecognized store outcome raise `PipelineContractException` naming the store instead of running the handler without a recorded claim.
- A blank key raises `IdempotencyConfigurationException` rather than being used, because every message with a blank key would share one key space. A non-null blank `Scope` is rejected the same way.
- A message declared with `KeyedByContext()` that reaches the shortcut with no key in context raises `IdempotencyConfigurationException` naming the message type.
- The shortcut records the grant as a `ClaimedIdempotencyKey` carrying the claim token and the replay decision, and the completion handler settles from that record without resolving again, so the derivation runs once per mediation and a handler-mutated message cannot fail settlement.
- The stored key defaults to the message full type name scope, escapes tenant, scope, and key segments before joining, and prefixes the mediation tenant when the mediation carries one. A command declared with `KeyedByContext()` that replays results sets a fingerprint with `WithFingerprint`, so a different request reusing a predictable key is denied rather than answered with the stored result.
- An unsettled claim is taken over with a new token by a new caller once it is older than the store's claim timeout, five minutes by default on the shipped stores. The timeout must be positive and is rejected with `ArgumentOutOfRangeException` at build time.
- `FromCommandKey` throws `InvalidOperationException` outside a keyed command's mediation and rejects a blank suffix, so a key is never invented silently.

## Non-Goals

- Durable envelope deduplication, which the inbox and outbox axes own with their own idempotency metadata.
- Key expiry for applied keys; retention is a storage job the application owns.
- A production store in the core packages; the in-memory store is a test double that forgets on restart and knows only its own process.

## Observability

No idempotency-specific meter or activity source. The `litebus.idempotency.store` probe reports `Unhealthy` when idempotency is enabled and no `IIdempotencyStore` is registered.

Operational alternatives:
- Read the store directly for applied keys, fingerprints, and in-flight claims.
- Correlate denials through the mediation decision counter, tagged with the deciding shortcut.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `A_repeated_command_is_answered_without_running_again` | `LiteBus.Mediator.UnitTests` |
| `A_different_key_runs_on_its_own` | `LiteBus.Mediator.UnitTests` |
| `A_command_declaring_no_key_is_untouched` | `LiteBus.Mediator.UnitTests` |
| `A_failed_command_releases_its_key_so_the_retry_runs` | `LiteBus.Mediator.UnitTests` |
| `The_key_is_scoped_so_two_message_types_do_not_collide` | `LiteBus.Mediator.UnitTests` |
| `A_repeat_replays_the_recorded_result_when_the_declaration_asks_for_it` | `LiteBus.Mediator.UnitTests` |
| `A_result_command_without_ReplayResult_names_the_fix_rather_than_inventing_an_answer` | `LiteBus.Mediator.UnitTests` |
| `The_probe_reports_a_missing_store` | `LiteBus.Mediator.UnitTests` |
| `The_probe_reports_a_registered_store` | `LiteBus.Mediator.UnitTests` |
| `A_repeat_whose_store_recorded_no_payload_names_the_store_as_the_fix` | `LiteBus.Mediator.UnitTests` |
| `A_blank_key_is_reported_rather_than_shared_by_every_message` | `LiteBus.Mediator.UnitTests` |
| `An_in_flight_command_is_denied_through_TrySendAsync` | `LiteBus.Mediator.UnitTests` |
| `An_in_flight_command_throws_through_SendAsync` | `LiteBus.Mediator.UnitTests` |
| `An_in_flight_result_command_is_denied_through_TrySendAsync` | `LiteBus.Mediator.UnitTests` |
| `A_fingerprint_mismatch_is_denied_through_TrySendAsync` | `LiteBus.Mediator.UnitTests` |
| `A_fingerprint_mismatch_throws_through_SendAsync` | `LiteBus.Mediator.UnitTests` |
| `A_matching_fingerprint_replays_without_running_again` | `LiteBus.Mediator.UnitTests` |
| `A_stale_claim_is_taken_over_so_the_command_runs` | `LiteBus.Mediator.UnitTests` |
| `A_conflicting_claim_is_never_released_by_the_loser` | `LiteBus.Mediator.UnitTests` |
| `A_denied_command_never_releases_an_in_flight_claim` | `LiteBus.Mediator.UnitTests` |
| `An_answered_mediation_releases_the_key_it_claimed` | `LiteBus.Mediator.UnitTests` |
| `BlankKey_PropagatesWithoutRunningErrorHandlers` | `LiteBus.Mediator.UnitTests` |
| `MissingContextKey_PropagatesWithoutRunningErrorHandlers` | `LiteBus.Mediator.UnitTests` |
| `A_key_resolved_by_a_guard_deduplicates_repeats` | `LiteBus.Mediator.UnitTests` |
| `A_different_resolved_key_runs_on_its_own` | `LiteBus.Mediator.UnitTests` |
| `The_expensive_derivation_runs_once_per_mediation` | `LiteBus.Mediator.UnitTests` |
| `A_declared_context_key_that_no_stage_supplied_is_reported` | `LiteBus.Mediator.UnitTests` |
| `Inside_a_keyed_command_FromCommandKey_returns_the_claimed_key` | `LiteBus.Mediator.UnitTests` |
| `Inside_a_keyed_command_FromCommandKey_honors_the_conflict_mode` | `LiteBus.Mediator.UnitTests` |
| `Inside_an_undeclared_command_FromCommandKey_throws` | `LiteBus.Mediator.UnitTests` |
| `FromCommandKey_outside_any_mediation_names_the_missing_scope` | `LiteBus.Mediator.UnitTests` |
| `FromCommandKey_rejects_a_blank_suffix` | `LiteBus.Mediator.UnitTests` |
| `ClaimCompleteClaim_ShouldGrantOnceThenReplayTheStoredPayload` | `LiteBus.Storage.IntegrationTests` |
| `SecondClaimWhileUnsettled_ShouldAnswerInFlight` | `LiteBus.Storage.IntegrationTests` |
| `AppliedKey_ShouldMismatchADifferentFingerprintAndReplayAMatch` | `LiteBus.Storage.IntegrationTests` |
| `ClaimOlderThanTheTimeout_ShouldBeTakenOver` | `LiteBus.Storage.IntegrationTests` |
| `Release_ShouldFreeAnUnsettledClaimButNeverAnAppliedKey` | `LiteBus.Storage.IntegrationTests` |
| `ConcurrentClaims_ShouldGrantExactlyOneAndReportTheOtherInFlight` | `LiteBus.Storage.IntegrationTests` |
| `SendAsync_ShouldRunTheHandlerOnceAndReplayTheStoredResult` | `LiteBus.Storage.IntegrationTests` |
| `Claim_ShouldGrantANewKey` | `LiteBus.Storage.IntegrationTests` |
| `CompletedKey_WithDifferentFingerprint_ShouldReportMismatch` | `LiteBus.Storage.IntegrationTests` |
| `CompletedKey_WithNullFingerprintOnEitherSide_ShouldReplay` | `LiteBus.Storage.IntegrationTests` |
| `UnsettledClaim_ShouldAnswerInFlightRegardlessOfFingerprint` | `LiteBus.Storage.IntegrationTests` |
| `ExpiredClaim_ShouldBeTakenOverByTheNextClaimant` | `LiteBus.Storage.IntegrationTests` |
| `ConcurrentClaims_ShouldGrantExactlyOneAndHoldTheOtherInFlight` | `LiteBus.Storage.IntegrationTests` |
| `ConcurrentTakeovers_ShouldGrantExactlyOne` | `LiteBus.Storage.IntegrationTests` |

### Untested

- Claim takeover while the first claimant is still settling, where both sides race the timeout boundary.
- Fingerprint comparison for very large message projections and store payload limits.

### Out-of-Scope

- Durable inbox and outbox deduplication behavior.
- Store schema management for the PostgreSQL idempotency tables.

## Deep Docs

- [In-process idempotency](../../concepts/idempotency.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Message definitions](../../concepts/message-definitions.md)
- [PostgreSQL idempotency store](../../integrations/commands-postgresql-idempotency-store.md)
