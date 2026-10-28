# Mediator

This section is the contributor contract reference for the mediator axis. Readers using LiteBus start with [the documentation overview](../../index.md).

The mediator axis provides in-process command, query, and event mediation on top of the shared message module. It covers the semantic contracts (`ICommand`, `IQuery<T>`, `IEvent`) and the pipeline stages around them: guard, validator, shortcut, pre, main, post, error, and completion. It also covers routing filters, handler priority, declarative message metadata, audit records, authorization positions, command idempotency, unit-of-work commits, fault refusal classification, and execution context behavior.

## Capability Index

| ID | Name | Maturity |
| --- | --- | --- |
| [mediator.commands](commands.md) | Commands | GA |
| [mediator.queries](queries.md) | Queries | GA |
| [mediator.events](events.md) | Events | GA |
| [mediator.handler-pipeline](handler-pipeline.md) | Handler pipeline | GA |
| [mediator.handler-filtering](handler-filtering.md) | Handler filtering | GA |
| [mediator.handler-priority](handler-priority.md) | Handler priority | GA |
| [mediator.mediation-settings](mediation-settings.md) | Mediation settings | GA |
| [mediator.recurring-commands](recurring-commands.md) | Recurring commands | GA |
| [mediator.module-registration](module-registration.md) | Module registration | GA |
| [mediator.execution-context](execution-context.md) | Execution context | GA |
| [mediator.generic-messages](generic-messages-and-handlers.md) | Generic messages and handlers | GA |
| [mediator.open-generic-handlers](open-generic-handlers.md) | Open generic handlers | GA |
| [mediator.polymorphic-dispatch](polymorphic-dispatch.md) | Polymorphic dispatch | GA |
| [mediator.message-definitions](message-definitions.md) | Message definitions and metadata | GA |
| [mediator.auditing](auditing.md) | Audit trail | GA |
| [mediator.authorization-declarations](authorization-declarations.md) | Authorization declarations and declaration guards | GA |
| [mediator.command-idempotency](command-idempotency.md) | Command idempotency | GA |
| [mediator.unit-of-work](unit-of-work.md) | Unit of work | GA |
| [mediator.fault-refusal-classification](fault-refusal-classification.md) | Fault refusal classification | GA |

## Package Map

| Package | Role |
| --- | --- |
| `LiteBus.Messaging` | Core message mediator, with the registry, resolve, and pipeline infrastructure |
| `LiteBus.Messaging.Abstractions` | Shared handler attributes, the execution context, and the mediation contracts |
| `LiteBus.Commands` + `LiteBus.Commands.Abstractions` | Command mediator and command semantics |
| `LiteBus.Queries` + `LiteBus.Queries.Abstractions` | Query mediator and stream query semantics |
| `LiteBus.Events` + `LiteBus.Events.Abstractions` | Event mediator and broadcast semantics |

## Test Projects and Suites

| Project | Suite focus |
| --- | --- |
| `LiteBus.Mediator.UnitTests` | End-to-end mediator behavior, guards, pipeline ordering, filtering, open generics, polymorphism |
| `LiteBus.Mediator.UnitTests` (`UseCases/Commands`) | Command module and command mediation tests |
| `LiteBus.Mediator.UnitTests` (`UseCases/Queries`) | Query module, stream query, and query validation tests |
| `LiteBus.Mediator.UnitTests` (`UseCases/Events`) | Event module, event execution, event validation tests |
| `LiteBus.Mediator.UnitTests` (`UseCases/Messaging`) | Message module and shared mediation behavior tests |
| `LiteBus.Mediator.UnitTests` (`Idempotency`) | Idempotency claim, denial, settlement, and context-supplied key tests |
| `LiteBus.Mediator.UnitTests` (`UnitOfWork`) | Unit-of-work commit gating tests for commands and queries |
| `LiteBus.Mediator.UnitTests` (`DeclarationGuards`, `Authorization`) | Declaration guard and authorization guard tests |
| `LiteBus.Mediator.UnitTests` (`FaultRefusals`) | Fault refusal classification tests |
| `LiteBus.Mediator.UnitTests` (`HandledAt`) | Mediation start instant tests |

## References

- [Command module](../../concepts/commands.md)
- [Query module](../../concepts/queries.md)
- [Event module](../../concepts/events.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Handler filtering](../../concepts/handler-filtering.md)
- [Execution context](../../concepts/execution-context.md)
- [Generic messages and handlers](../../concepts/generic-messages-and-handlers.md)
- [Open generic handlers](../../concepts/open-generic-handlers.md)
- [Polymorphic dispatch](../../concepts/polymorphic-dispatch.md)
- [Authorization declarations](../../concepts/authorization-declarations.md)
- [In-process idempotency](../../concepts/idempotency.md)
- [Domain events and unit of work](../../reliable-messaging/domain-events-and-unit-of-work.md)
