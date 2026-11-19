# Auditing

- **ID**: `mediator.auditing`
- **Name**: Audit trail
- **Maturity**: GA
- **Summary**: Writes one audit record per mediation from declarative metadata, on every outcome path including refusals and cancellations.
- **Guide**: [Auditing](../../concepts/auditing.md)

## What It Does

`AddAuditing` configures the trail, the actor resolver, the outcome mapper, and which axes produce records. Selecting an axis registers a completion handler there. When a mediation ends, the handler reads the message's `AuditDeclaration` from message metadata, combines it with the resolved actor and the detail the handler pushed through `IAuditScope`, and hands an `AuditRecord` to the application's `IAuditTrail`.

Because the writer runs at the completion stage, refusals, failures, and cancellations produce records in the same way successes do. That is the difference between an audit trail and a changelog. An audit answers who attempted something and was stopped. A handler-written record can never capture that, because authorization refuses before the handler reaches its audit line.

The completion stage is not cancellable, so a cancelled mediation still produces its record. Attribution runs there for the same reason. A resolver at the completion stage names the actor on a denied command, where a pre-stage handler never runs.

The constant half of a record is declared once per message, with `[Audited]` and `[AuditExempt]` or with an `IAuditDefinition<TMessage>`. Both contribute an `AuditDeclaration`, so the writer reads one metadata key, and a definition overwrites an attribute rather than sitting beside it.

The variable half, such as an identifier the handler generated, is pushed at runtime through `IAuditScope`. When the value can be read from the message itself, it is declared with a target or reason selector instead: `AuditedDeclaration.WithTargetFrom<TMessage>` and `WithReasonFrom<TMessage>` project `TargetId` and `Reason` from the message. Selectors are delegates, so attributes cannot carry them. A message that reads its target or reason from itself needs a definition.

The completion context carries the identity the mediation ran for as `MessageCompletionContext.Identity`, copied from `IExecutionContext.Identity` when the context is built. The writer and the documented `IAuditActorResolver` read the caller from there rather than from ambient state. When no resolver is registered, the writer falls back to the principal on the completion identity, reading the identifier from the `NameIdentifier` claim and then the `sub` claim; when the principal carries no usable claim, the record keeps no actor.

Tamper evidence below the record is a set of replaceable chain pieces with LiteBus defaults: the stored document (`IAuditRecordMapper<TDocument>`), the segmenting (`IAuditSegmenter<TDocument>`), the canonical bytes (`IAuditCanonicalizer<TDocument>`), the link rule (`IAuditLinkRule`), the hasher (`IAuditHasher`), the checkpoint policy (`ICheckpointPolicy`), the signer (`ICheckpointSigner`), and the publisher (`ICheckpointPublisher`). Each record stores the name of the `AuditHashScheme<TDocument>` it was written with, and `IAuditChain` verifies, checkpoints, and expires segments. See [Auditing](../../concepts/auditing.md#hash-chained-audit-storage).

## Public Surface

```csharp
[Audited("orders.place-order", Category = "money", TargetKind = "order")]
public sealed record PlaceOrderCommand(Guid CartId) : ICommand<OrderId>;

services.AddLiteBus(registry =>
{
    registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
        .UseTrail<PostgresAuditTrail>()
        .UseActorResolver<RequestActorResolver>()
        .ForCommands()
        .ForQueries()));
});
```

| API | Role |
| --- | --- |
| `AuditedAttribute` / `AuditExemptAttribute` | Attribute-form audit declaration |
| `IAuditDefinition<TMessage>` | Definition-form audit declaration |
| `AuditDeclaration` | The closed base of the two positions stored in message metadata |
| `AuditedDeclaration` / `AuditExemptDeclaration` | The audited position and the recorded exemption |
| `AuditedDeclaration.WithTargetFrom<TMessage>` / `WithReasonFrom<TMessage>` | Declare the target and reason as projections from the message, read into `TargetSelector` and `ReasonSelector` |
| `IAuditScope` | Handler-supplied actor, target, reason, and properties |
| `AuditRecord` / `AuditOutcome` | The record handed to the trail |
| `AuditActor` | Who acted: a required `Id` and `Kind`, plus `DisplayName` and `OnBehalfOf`. Factories `User(id)`, `System(processName)`, `For(kind, id)`. A display name is added with `with` |
| `IAuditRecordWriter` | The whole contract between the pipeline and auditing. Replace it to own the record shape |
| `IAuditActorResolver` | Application-supplied attribution, reading the message and `MessageCompletionContext.Identity` at the completion stage |
| `MessageCompletionContext.Identity` | The identity the mediation ran for, copied from `IExecutionContext.Identity`; `ExecutionIdentity.Anonymous` until set on a hand-built context. Both generic views expose the same value |
| `IAuditTrail` | Application-supplied sink |
| `MessageModuleBuilder.AddAuditing(Action<AuditingBuilder>)` | Configures the trail, the actor resolver, the outcome mapper, and the axes |
| `AuditingBuilder` | `UseTrail`, `UseTrailInstance`, `UseActorResolver`, `UseActorResolverInstance`, `UseOutcomeMapper`, `UseOutcomeMapperInstance`, `UseRecordWriter`, `UseRecordWriterInstance`, `ForCommands`, `ForQueries`, `ForEvents`, `ForAllAxes` |
| `MessageModuleBuilder.UseAuditRecordWriter<T>(lifetime)` / `UseAuditRecordWriterInstance` | Replaces the record building. The probe then reports the writer instead of demanding a trail |
| `MessageModuleBuilder.UseAuditTrail<T>(lifetime)` | The primitive `AddAuditing` composes. Scoped by default |
| `MessageModuleBuilder.UseAuditTrailInstance(trail)` | Registers a trail you already hold, as a singleton |
| `MessageModuleBuilder.UseAuditActorResolver<T>(lifetime)` / `UseAuditActorResolverInstance` | The attribution primitives |
| `MessageModuleBuilder.UseAuditOutcomeMapper<T>()` / `UseAuditOutcomeMapper(instance)` | Classifies an application refusal exception as a denial rather than a failure |
| `MessageModuleBuilder.RequireUniqueAuditActions()` | Fails composition when two messages share an action code |
| `MessageModuleBuilder.RequireAuditActionFormat(pattern)` | Fails composition when an action breaks the house naming convention. The default pattern is `MessageModuleBuilder.DefaultAuditActionPattern` |
| `IAuditOutcomeMapper` / `DefaultAuditOutcomeMapper` | Classifies a refusal that arrived as an exception as denied rather than failed |
| `AuditReasonMissingException` | Raised when a reason-required action supplies none |
| `AuditTrailDiagnosticCheck` | Probe reporting `litebus.audit.trail`. It reports unhealthy with no trail and degraded with no actor resolver |
| `IMessageCatalog` | Registered as a Singleton, so the declarations are readable at runtime as well as at composition |
| `AuditCatalogueRow` / `AuditCatalogue.ToRows` / `AuditCatalogue.ToMarkdown` | The audit catalogue, derived from the declarations |
| `CommandModuleBuilder` / `QueryModuleBuilder` / `EventModuleBuilder` `.EnableAuditing` | Registers the writer and the probe on one axis |
| `IAuditChainDocument` | The chain fields every stored audit document carries: `Segment`, `Sequence`, `PreviousHash`, `Hash`, `HashScheme` |
| `IAuditRecordMapper<TDocument>` | Maps an `AuditRecord` to the stored document type |
| `IAuditSegmenter<TDocument>` | Names the segment per document; `CreateGenesis` opens a segment with a genesis record, or `null` for none |
| `IAuditCanonicalizer<TDocument>` | Produces the exact bytes hashed; excludes `Hash` |
| `IAuditLinkRule` / `AuditLinkInput` | Combines canonical bytes with segment, sequence, and previous hash into the bytes hashed |
| `IAuditHasher` | Computes the hash: `Sha256AuditHasher`, or `HmacSha256AuditHasher` with a fixed key or a per-operation key provider |
| `AuditHashScheme<TDocument>` | A named combination of canonicalizer, link rule, and hasher; each record stores the name it was written with |
| `ICheckpointPolicy` / `AuditCheckpointCandidate` | Decides when a checkpoint is due; `IsTimeBased` marks a policy the chain also asks on a schedule. `CheckpointPolicies.EveryRecords`, `Every`, `Any` |
| `AuditCheckpoint` / `AuditCheckpointKind` / `AuditCheckpointSignature` | The anchored position, hash, count, and signature; `Regular` or `Final` |
| `ICheckpointSigner` | Signs checkpoints and verifies their signatures |
| `ICheckpointPublisher` | Publishes checkpoints to an independent store |
| `ICheckpointSource` | Reads the latest checkpoint of a segment for verification |
| `AuditChainVerification` / `AuditChainBreak` / `AuditChainBreakKind` | The verify result: every break in sequence order, the head, and the checkpoint comparison |
| `IAuditChain` | `VerifyAsync`, `CutCheckpointAsync`, `ExpireSegmentAsync` |
| `AuditChainVerifier<TDocument>` | The storage-free verifier every store reuses |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`
- `LiteBus.Commands`
- `LiteBus.Queries`
- `LiteBus.Events`

## Requires

- `mediator.handler-pipeline`
- `mediator.message-definitions`
- `mediator.execution-context`

## Invariants

- A record is produced only when the message declares an audited position. An exempt or undeclared message produces none.
- Records are produced on every outcome path: success, early answer, denial, failure, and cancellation.
- A fault an error handler recovered is recorded as `Failed`, with no exception on the completion context.
- An unregistered runtime type mediated through a base descriptor is audited under that descriptor's declaration, so scan coverage never decides whether a mediation leaves a record.
- A guard denial is recorded as `AuditOutcome.Denied`, a validation failure as `AuditOutcome.Invalid`, and a shortcut answer as `AuditOutcome.Succeeded`, with no mapper involved.
- The writer runs at `HandlerPriorities.Observability`, after LiteBus persistence handlers and after unannotated application handlers.
- A record for a successful action whose declaration sets `ReasonRequired` is never written without a reason. The writer raises `AuditReasonMissingException` instead, before the commit at `HandlerPriorities.UnitOfWork`, so the work is rolled back.
- An exception raised while writing a record cannot replace the original fault. It is attached to it under `MediationExceptionData.SuppressedCompletionFaults`.
- `IAuditScope` state lives on the ambient execution context, so concurrent mediations never share it.
- `AuditRecord` carries no before-and-after payload snapshot, and does not carry the message. The message is handed to `IAuditActorResolver` instead.
- An actor pushed through `IAuditScope.WithActor` overrides whatever the resolver resolved. A resolver returning null leaves `AuditRecord.Actor` null, which means nothing established an actor.
- The writer consults the scope first and runs a target or reason selector only when the scope is silent, so a handler-supplied value wins. For the reason, the pipeline decision sits between the two, because a guard denial or an answer explanation describes the ending while the selector describes the intent.
- A selector result that is null or blank means the message carries no value, and `ReasonRequired` applies to the final value, so a required reason may come from the scope or the selector alike. A selector that throws fails the record write.
- An audited event writes one record per publish, not one per handler.
- The completion identity is copied from the mediation execution context, defaults to `ExecutionIdentity.Anonymous`, and reads the same on both generic views. With no resolver registered, the writer attributes from the principal on that identity (`NameIdentifier`, then `sub`); an explicit resolver result is never replaced by the fallback.
- Each chained record stores the name of the scheme it was written with, and verification picks the scheme per record, so a scheme change from a date onward verifies without rewriting history.
- Verification collects every break, in sequence order, and compares the segment against its latest checkpoint, so a rewritten suffix fails the checkpoint hash and count comparison even when its links are consistent.
- A chain that shares one scheme name between two different schemes fails at build time.

## Non-Goals

- Persistence, integrity chaining, retention, and the read side. These belong in the `IAuditTrail` implementation.
- Modelling identity. `AuditActor` is a stable identifier, a kind, an optional display name, and an optional delegating actor. What those mean and how they resolve is the application's, supplied through `IAuditActorResolver`.
- Publishing the trail through the outbox. The outbox provides at-least-once delivery to other systems, while evidence needs durability at the source.
- Deriving audit records from domain events, which carry domain identity rather than use-case identity and do not exist for refused actions.

## Observability

No audit-specific meter or activity source. The record itself carries `Duration`, `CorrelationId`, and `MessageType` for correlation with the diagnostic log.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `An_attribute_declared_command_produces_a_record_with_handler_supplied_detail` | `LiteBus.Mediator.UnitTests` |
| `A_definition_declared_command_produces_a_record` | `LiteBus.Mediator.UnitTests` |
| `A_definition_takes_precedence_over_an_attribute` | `LiteBus.Mediator.UnitTests` |
| `An_exempt_command_produces_no_record` | `LiteBus.Mediator.UnitTests` |
| `A_refusal_is_recorded_as_a_failure_by_default` | `LiteBus.Mediator.UnitTests` |
| `A_refusal_is_recorded_as_a_denial_when_an_outcome_mapper_says_so` | `LiteBus.Mediator.UnitTests` |
| `A_gate_denial_is_recorded_as_a_denial_without_any_outcome_mapper` | `LiteBus.Mediator.UnitTests` |
| `An_early_answer_is_recorded_as_a_success_rather_than_a_denial` | `LiteBus.Mediator.UnitTests` |
| `A_cancelled_mediation_still_produces_its_record` | `LiteBus.Mediator.UnitTests` |
| `A_declaration_over_a_marker_interface_covers_the_messages_beneath_it` | `LiteBus.Mediator.UnitTests` |
| `A_required_reason_that_the_handler_supplies_is_recorded` | `LiteBus.Mediator.UnitTests` |
| `A_required_reason_that_goes_missing_is_reported_rather_than_recorded_as_absent` | `LiteBus.Mediator.UnitTests` |
| `The_audit_probe_reports_unhealthy_when_no_trail_is_registered` | `LiteBus.Mediator.UnitTests` |
| `An_audited_query_produces_a_record` | `LiteBus.Mediator.UnitTests` |
| `Selectors_supply_the_target_and_reason_when_the_handler_pushes_nothing` | `LiteBus.Mediator.UnitTests` |
| `The_scope_wins_when_both_the_scope_and_a_selector_supply_a_value` | `LiteBus.Mediator.UnitTests` |
| `A_selector_is_never_invoked_when_the_scope_supplied_the_value` | `LiteBus.Mediator.UnitTests` |
| `A_null_or_blank_selector_result_leaves_the_value_empty` | `LiteBus.Mediator.UnitTests` |
| `A_required_reason_satisfied_by_a_selector_passes` | `LiteBus.Mediator.UnitTests` |
| `A_required_reason_missing_from_the_selector_fails_like_a_missing_scope_reason` | `LiteBus.Mediator.UnitTests` |
| `A_throwing_selector_fails_the_record_write` | `LiteBus.Mediator.UnitTests` |
| `Completion_carries_the_settings_identity` | `LiteBus.Mediator.UnitTests` |
| `Completion_defaults_to_anonymous_when_no_identity_is_resolved` | `LiteBus.Mediator.UnitTests` |
| `Typed_completion_views_expose_the_same_identity` | `LiteBus.Mediator.UnitTests` |
| `An_actor_resolver_sees_the_request_identity` | `LiteBus.Mediator.UnitTests` |
| `A_manually_built_completion_context_defaults_to_anonymous` | `LiteBus.Mediator.UnitTests` |
| `An_intact_chain_verifies_with_no_breaks` | `LiteBus.Mediator.UnitTests` |
| `An_empty_segment_verifies_intact` | `LiteBus.Mediator.UnitTests` |
| `A_missing_sequence_is_reported_as_a_gap` | `LiteBus.Mediator.UnitTests` |
| `A_repeated_sequence_is_reported_as_a_duplicate` | `LiteBus.Mediator.UnitTests` |
| `A_misplaced_sequence_is_reported_as_a_duplicate` | `LiteBus.Mediator.UnitTests` |
| `A_tampered_document_is_reported_as_a_hash_mismatch` | `LiteBus.Mediator.UnitTests` |
| `A_rewritten_previous_hash_is_reported` | `LiteBus.Mediator.UnitTests` |
| `An_unknown_scheme_is_reported` | `LiteBus.Mediator.UnitTests` |
| `A_document_naming_no_scheme_is_reported` | `LiteBus.Mediator.UnitTests` |
| `Two_schemes_in_one_segment_verify` | `LiteBus.Mediator.UnitTests` |
| `A_chain_missing_one_of_its_schemes_reports_unknown_scheme` | `LiteBus.Mediator.UnitTests` |
| `A_matching_checkpoint_compares_equal` | `LiteBus.Mediator.UnitTests` |
| `A_rewritten_checkpoint_hash_is_reported` | `LiteBus.Mediator.UnitTests` |
| `A_checkpoint_count_above_the_segment_is_reported` | `LiteBus.Mediator.UnitTests` |
| `A_missing_covered_record_breaks_the_checkpoint_count` | `LiteBus.Mediator.UnitTests` |
| `A_checkpoint_for_a_missing_sequence_breaks_the_checkpoint_hash` | `LiteBus.Mediator.UnitTests` |
| `A_signed_checkpoint_verifies` | `LiteBus.Mediator.UnitTests` |
| `A_tampered_signed_checkpoint_is_reported` | `LiteBus.Mediator.UnitTests` |
| `An_unsigned_checkpoint_with_a_signer_is_reported_as_an_invalid_signature` | `LiteBus.Mediator.UnitTests` |
| `Without_a_signer_an_unsigned_checkpoint_leaves_validity_unknown` | `LiteBus.Mediator.UnitTests` |
| `Every_break_is_reported_not_only_the_first` | `LiteBus.Mediator.UnitTests` |
| `An_empty_segment_misses_its_checkpoint` | `LiteBus.Mediator.UnitTests` |
| `A_lookup_with_a_null_scheme_is_rejected` | `LiteBus.Mediator.UnitTests` |
| `Schemes_with_a_repeated_name_are_rejected` | `LiteBus.Mediator.UnitTests` |
| `A_blank_segment_is_rejected` | `LiteBus.Mediator.UnitTests` |
| `EveryRecords_rejects_a_non_positive_interval` | `LiteBus.Mediator.UnitTests` |
| `EveryRecords_is_due_once_the_interval_is_reached` | `LiteBus.Mediator.UnitTests` |
| `EveryRecords_is_not_time_based` | `LiteBus.Mediator.UnitTests` |
| `Every_rejects_a_non_positive_interval` | `LiteBus.Mediator.UnitTests` |
| `Every_rejects_a_negative_interval` | `LiteBus.Mediator.UnitTests` |
| `Every_is_due_once_the_interval_has_passed` | `LiteBus.Mediator.UnitTests` |
| `Every_is_due_for_records_without_a_checkpoint` | `LiteBus.Mediator.UnitTests` |
| `Every_is_never_due_without_records` | `LiteBus.Mediator.UnitTests` |
| `Every_is_time_based` | `LiteBus.Mediator.UnitTests` |
| `Any_is_due_when_any_policy_is_due` | `LiteBus.Mediator.UnitTests` |
| `Any_without_policies_is_never_due` | `LiteBus.Mediator.UnitTests` |
| `Any_is_time_based_when_any_policy_is` | `LiteBus.Mediator.UnitTests` |
| `Any_rejects_a_null_entry` | `LiteBus.Mediator.UnitTests` |
| `The_same_key_and_bytes_hash_to_the_same_value` | `LiteBus.Mediator.UnitTests` |
| `Different_keys_hash_differently` | `LiteBus.Mediator.UnitTests` |
| `The_keyed_hash_differs_from_the_plain_digest` | `LiteBus.Mediator.UnitTests` |
| `The_key_provider_supplies_the_key` | `LiteBus.Mediator.UnitTests` |
| `The_provider_is_asked_per_operation_so_a_rotation_takes_effect` | `LiteBus.Mediator.UnitTests` |
| `An_empty_key_is_rejected` | `LiteBus.Mediator.UnitTests` |
| `A_null_provider_is_rejected` | `LiteBus.Mediator.UnitTests` |
| `A_provider_returning_no_key_fails_the_hash` | `LiteBus.Mediator.UnitTests` |
| `A_cancelled_token_throws` | `LiteBus.Mediator.UnitTests` |
| `The_canonical_bytes_pass_through_unchanged` | `LiteBus.Mediator.UnitTests` |
| `A_null_input_is_rejected` | `LiteBus.Mediator.UnitTests` |
| `The_same_bytes_hash_to_the_same_value` | `LiteBus.Mediator.UnitTests` |
| `Different_bytes_hash_differently` | `LiteBus.Mediator.UnitTests` |
| `The_hash_matches_the_published_SHA256_vector` | `LiteBus.Mediator.UnitTests` |
| `The_hash_is_lowercase_hex_of_64_characters` | `LiteBus.Mediator.UnitTests` |

### Untested

- Audit records produced from mediation driven by the inbox and outbox, where correlation and tenant come from the stored envelope.
- Stream query auditing across partially enumerated results.

### Out-of-Scope

- Storage adapters for audit records.
- Tamper-evidence and retention enforcement.

## References

- [Auditing](../../concepts/auditing.md)
- [Message definitions](../../concepts/message-definitions.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Commands Marten Adapter](../storage/storage.commands.adapter.marten.md)
