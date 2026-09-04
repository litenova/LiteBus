# Auditing

- **ID**: `mediator.auditing`
- **Name**: Audit trail
- **Maturity**: GA
- **Summary**: Produces audit records at the mediation boundary from declarative metadata, on every outcome including refusals and cancellations.

## What It Does

`AddAuditing` configures the trail, the actor resolver, the outcome mapper, and which axes produce records. Selecting an axis registers a completion handler there. When a mediation ends, the handler reads the message's `AuditDeclaration` from message metadata, combines it with the actor the resolver established and the detail the handler pushed through `IAuditScope`, and hands an `AuditRecord` to the application's `IAuditTrail`.

Because it runs at the completion stage, refusals, failures, and cancellations produce records just as successes do. That is the difference between an audit trail and a changelog: an audit exists to answer who attempted something and was stopped, and a handler-written record can never capture that, since authorization refuses before the handler reaches its audit line. The stage is not cancellable, so a cancelled mediation still produces its record. Attribution runs there for the same reason: a resolver at the completion stage names the actor on a denied command, and a pre-stage handler never runs on that path.

The constant half of a record is declared once per message, with `[Audited]` and `[AuditExempt]` or with an `IAuditDefinition<TMessage>`. Both contribute an `AuditDeclaration`, so the writer reads one metadata key and a definition overwrites an attribute rather than sitting beside it. The variable half, such as an identifier the handler generated, is pushed at runtime.

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
| `IAuditScope` | Handler-supplied actor, target, reason, and properties |
| `AuditRecord` / `AuditOutcome` | The record handed to the trail |
| `AuditActor` | Who acted: a required `Id` and `Kind`, plus `DisplayName` and `OnBehalfOf`. Factories `User(id)`, `System(processName)`, `For(kind, id)`; a display name is added with `with` |
| `IAuditRecordWriter` | The whole contract between the pipeline and auditing. Replace it to own the record shape |
| `IAuditActorResolver` | Application-supplied attribution, reading the message at the completion stage |
| `IAuditTrail` | Application-supplied sink |
| `MessageModuleBuilder.AddAuditing(Action<AuditingBuilder>)` | Configures the whole feature: trail, actor resolver, outcome mapper, and axes |
| `AuditingBuilder` | `UseTrail`, `UseTrailInstance`, `UseActorResolver`, `UseActorResolverInstance`, `UseOutcomeMapper`, `UseOutcomeMapperInstance`, `UseRecordWriter`, `UseRecordWriterInstance`, `ForCommands`, `ForQueries`, `ForEvents`, `ForAllAxes` |
| `MessageModuleBuilder.UseAuditRecordWriter<T>(lifetime)` / `UseAuditRecordWriterInstance` | Replaces the record building. The probe then reports the writer instead of demanding a trail |
| `MessageModuleBuilder.UseAuditTrail<T>(lifetime)` | The primitive `AddAuditing` composes. Scoped by default |
| `MessageModuleBuilder.UseAuditTrailInstance(trail)` | Registers a trail you already hold, as a singleton |
| `MessageModuleBuilder.UseAuditActorResolver<T>(lifetime)` / `UseAuditActorResolverInstance` | The attribution primitives |
| `MessageModuleBuilder.UseAuditOutcomeMapper<T>()` / `UseAuditOutcomeMapper(instance)` | Classifies an application refusal exception as a denial rather than a failure |
| `MessageModuleBuilder.RequireUniqueAuditActions()` | Fails composition when two messages share an action code |
| `MessageModuleBuilder.RequireAuditActionFormat(pattern)` | Fails composition when an action breaks the house naming convention |
| `IAuditRecordWriter` | Composes a record from a completion context |
| `IAuditOutcomeMapper` / `DefaultAuditOutcomeMapper` | Classifies an exception-borne refusal as denied rather than failed |
| `AuditReasonMissingException` | Raised when a reason-required action supplies none |
| `AuditTrailDiagnosticCheck` | Probe reporting `litebus.audit.trail`: unhealthy with no trail, degraded with no actor resolver |
| `IMessageCatalog` | Registered as a Singleton, so the declarations are readable at runtime as well as at composition |
| `AuditCatalogueRow` / `AuditCatalogue.ToRows` / `AuditCatalogue.ToMarkdown` | The audit catalogue, derived from the declarations |
| `CommandModuleBuilder` / `QueryModuleBuilder` / `EventModuleBuilder` `.EnableAuditing` | Registers the writer and the probe on one axis |

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

- A record is produced only when the message declares an audited position; an exempt or undeclared message produces none.
- Records are produced on every outcome path: success, early answer, denial, failure, and cancellation.
- A guard denial is recorded as `AuditOutcome.Denied`, a validation failure as `AuditOutcome.Invalid`, and a shortcut answer as `AuditOutcome.Succeeded`, with no mapper involved.
- The writer runs at `HandlerPriorities.Observability`, after LiteBus persistence handlers and after unannotated application handlers.
- A record for a successful action whose declaration sets `ReasonRequired` is never written without a reason; the writer raises `AuditReasonMissingException` instead, before the commit at `HandlerPriorities.UnitOfWork`, so the work is rolled back.
- An exception raised while writing a record cannot replace the original fault; it is attached to it under `MediationExceptionData.SuppressedCompletionFaults`.
- `IAuditScope` state lives on the ambient execution context, so concurrent mediations never share it.
- `AuditRecord` carries no before-and-after payload snapshot, and does not carry the message. The message is handed to `IAuditActorResolver` instead.
- An actor pushed through `IAuditScope.WithActor` overrides whatever the resolver resolved; a resolver returning null leaves `AuditRecord.Actor` null, which means nothing established an actor.
- An audited event writes one record per publish, not one per handler.

## Non-Goals

- Persistence, integrity chaining, retention, and the read side. These belong in the `IAuditTrail` implementation.
- Modelling identity. `AuditActor` is a stable identifier, a kind, an optional display name, and an optional delegating actor; what those mean and how they resolve is the application's, supplied through `IAuditActorResolver`.
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

### Untested

- Audit records produced from inbox and outbox driven mediation, where correlation and tenant come from the stored envelope.
- Stream query auditing across partially enumerated results.

### Out-of-Scope

- Storage adapters for audit records.
- Tamper-evidence and retention enforcement.

## Deep Docs

- [Auditing](../../concepts/auditing.md)
- [Message definitions](../../concepts/message-definitions.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
