# Mediator Axis Capability Catalog

The mediator axis provides in-process command, query, and event mediation on top of the shared message module. It covers semantic contracts (`ICommand`, `IQuery<T>`, `IEvent`), pipeline stages (pre/main/post/error), routing filters, handler priority, and execution context behavior.

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
| [mediator.module-registration](module-registration.md) | Module registration | GA |
| [mediator.execution-context](execution-context.md) | Execution context | GA |
| [mediator.generic-messages](generic-messages-and-handlers.md) | Generic messages and handlers | GA |
| [mediator.open-generic-handlers](open-generic-handlers.md) | Open generic handlers | GA |
| [mediator.polymorphic-dispatch](polymorphic-dispatch.md) | Polymorphic dispatch | GA |

## Package Map

| Package | Role |
| --- | --- |
| `LiteBus.Messaging` | Core message mediator, registry, resolve and pipeline infrastructure |
| `LiteBus.Messaging.Abstractions` | Shared handler attributes, execution context, mediation contracts |
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

## Deep Docs

- [Command module](../../concepts/commands.md)
- [Query module](../../concepts/queries.md)
- [Event module](../../concepts/events.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Handler filtering](../../concepts/handler-filtering.md)
- [Execution context](../../concepts/execution-context.md)
- [Generic messages and handlers](../../concepts/generic-messages-and-handlers.md)
- [Open generic handlers](../../concepts/open-generic-handlers.md)
- [Polymorphic dispatch](../../concepts/polymorphic-dispatch.md)
