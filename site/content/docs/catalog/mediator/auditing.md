# Auditing

- **ID**: `mediator.auditing`
- **Name**: Audit trail
- **Maturity**: GA
- **Summary**: Produces audit records at the mediation boundary from declarative metadata, on every outcome including refusals.

## What It Does

`EnableAuditing()` registers a completion handler on the command or query axis. When a mediation ends, the handler reads the message's `AuditDeclaration` from message metadata, combines it with detail the handler pushed through `IAuditScope`, and hands an `AuditRecord` to the application's `IAuditTrail`.

Because it runs at the completion stage, refusals, failures, and cancellations produce records just as successes do. That is the difference between an audit trail and a changelog: an audit exists to answer who attempted something and was stopped, and a handler-written record can never capture that, since authorization aborts or throws before the handler reaches its audit line.

The constant half of a record is declared once per message, with `[Audited]` and `[AuditExempt]` or with an `IAuditDefinition<TMessage>` facet. The variable half, such as an identifier the handler generated, is pushed at runtime.

## Public Surface

```csharp
[Audited("orders.place-order", Category = "money", TargetKind = "order")]
public sealed record PlaceOrderCommand(Guid CartId) : ICommand<OrderId>;

services.AddSingleton<IAuditTrail, PostgresAuditTrail>();

services.AddLiteBus(registry =>
{
    registry.AddMessaging(_ => { });
    registry.AddCommands(builder => builder.EnableAuditing());
    registry.AddQueries(builder => builder.EnableAuditing());
});
```

| API | Role |
| --- | --- |
| `AuditedAttribute` / `AuditExemptAttribute` | Attribute-form audit declaration |
| `IAuditDefinition<TMessage>` | Definition-form audit declaration |
| `AuditDeclaration` | The resolved declaration stored in message metadata |
| `IAuditScope` | Handler-supplied target, reason, and properties |
| `AuditRecord` / `AuditOutcome` | The record handed to the trail |
| `IAuditTrail` | Application-supplied sink |
| `IAuditOutcomeMapper` / `DefaultAuditOutcomeMapper` | Classifies a refusal as denied rather than failed |
| `CommandModuleBuilder.EnableAuditing` / `QueryModuleBuilder.EnableAuditing` | Registers the writer on an axis |
| `MessageModuleBuilder.UseAuditOutcomeMapper` | Replaces the default outcome mapper |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`
- `LiteBus.Commands`
- `LiteBus.Queries`

## Requires

- `mediator.handler-pipeline`
- `mediator.message-definitions`
- `mediator.execution-context`

## Invariants

- A record is produced only when the message declares an audited position; an exempt or undeclared message produces none.
- Records are produced on every outcome path: success, denial, failure, and cancellation.
- The writer runs at `LiteBusHandlerPriority.Observability`, after LiteBus persistence handlers and after unannotated application handlers.
- An exception raised while writing a record cannot replace the original fault, because completion handlers suppress on an already-faulted mediation.
- `IAuditScope` state lives on the ambient execution context, so concurrent mediations never share it.
- `AuditRecord` carries no before-and-after payload snapshot.

## Non-Goals

- Persistence, integrity chaining, retention, and the read side. These belong in the `IAuditTrail` implementation.
- Modelling identity. LiteBus does not know who the actor is; the trail implementation captures it.
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
