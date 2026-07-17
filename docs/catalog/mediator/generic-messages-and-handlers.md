# Generic Messages and Handlers

- **ID**: `mediator.generic-messages`
- **Name**: Generic messages and handlers
- **Maturity**: GA
- **Summary**: Supports generic command/query/event message shapes with closed handlers resolved per concrete type.

## What It Does

LiteBus supports generic message contracts such as `CreateEntityCommand<TEntity, TKey>` and matching closed handlers. Generic message types are normalized by generic type definition in the message registry, while runtime mediation resolves the most specific compatible descriptor for each concrete closed message.

This enables shared behavior and reduced duplication for repeated CRUD or projection patterns while keeping typed handler contracts.

## Public Surface

```csharp
public sealed record GetByIdQuery<TEntity>(Guid Id) : IQuery<TEntity>;

public sealed class GetByIdQueryHandler<TEntity> : IQueryHandler<GetByIdQuery<TEntity>, TEntity>
    where TEntity : class, new()
{
    public Task<TEntity> HandleAsync(GetByIdQuery<TEntity> query, CancellationToken cancellationToken = default)
        => Task.FromResult(new TEntity());
}
```

| API | Role |
| --- | --- |
| `ICommand<TResult>` with generic message type | Generic command contract |
| `IQuery<TResult>` / `IStreamQuery<TResult>` with generic message type | Generic query contract |
| `IEvent` or typed plain event with generic payload | Generic event contract |
| `RegisterFromAssembly(...)` on semantic builders | Registers generic messages/handlers discovered in assembly |
| `MessageRegistry.Find(Type)` | Normalizes generic lookup through generic type definitions |

## Packages

- `LiteBus.Commands` / `LiteBus.Commands.Abstractions`
- `LiteBus.Queries` / `LiteBus.Queries.Abstractions`
- `LiteBus.Events` / `LiteBus.Events.Abstractions`
- `LiteBus.Messaging`

## Requires

- `mediator.module-registration`
- `mediator.polymorphic-dispatch`

## Invariants

- Generic message handlers must still satisfy single-handler rule for command/query.
- Generic constraints control valid concrete message-handler combinations.
- Registry keeps normalized generic type descriptors for lookup consistency.

## Non-Goals

- Auto-generation of generic CRUD message families.
- Schema registry for generic type evolution.
- Durable open generic contract registration without closed type selection.

## Observability

No dedicated telemetry for generic message closing or dispatch is exposed.

Operational alternatives:
- Use handler-level logs with concrete `typeof(T)` metadata.
- Validate coverage through integration and unit tests for representative closed types.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Send_LogActivityCommand_ShouldGoThroughHandlersCorrectly` | `LiteBus.Mediator.UnitTests` |
| `Mediating_GetProductByCriteriaQuery_ShouldGoThroughHandlersCorrectly` | `LiteBus.Mediator.UnitTests` |
| `mediating_generic_event_goes_through_registered_handlers_correctly` | `LiteBus.Mediator.UnitTests` |
| `Mediating_StreamQuery_WithIndirectHandler_ShouldUseBaseTypeHandler` | `LiteBus.Mediator.UnitTests` |

### Untested

- Deeply nested generic arguments in stream query scenarios.
- Generic event publish with high parallel handler counts and aggregate failures.

### Out-of-Scope

- Durable contract migration strategy for many closed generic message variants.
- Compile-time code generation for generic mediator contracts.

## Deep Docs

- [Generic messages and handlers](../../concepts/generic-messages-and-handlers.md)
- [Open generic handlers](../../concepts/open-generic-handlers.md)
