# Mediation Settings

- **ID**: `mediator.mediation-settings`
- **Name**: Mediation settings
- **Maturity**: GA
- **Summary**: Provides per-call routing, execution, and context item settings for command, query, and event mediation.

## What It Does

Each semantic mediator accepts an optional settings object:
- `CommandMediationSettings`
- `QueryMediationSettings`
- `EventMediationSettings`

Settings carry:
- Routing (`Tags`, `HandlerPredicate`) for all message kinds.
- Items dictionary for ambient execution context state.
- Event-specific execution and no-handler behavior.

## Public Surface

```csharp
var settings = new EventMediationSettings
{
    ThrowIfNoHandlerFound = true,
    Routing = new EventRoutingSettings
    {
        Tags = ["Notifications"]
    },
    Execution = new EventExecutionSettings
    {
        PriorityGroupsConcurrencyMode = ConcurrencyMode.Sequential,
        HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel,
        ParallelFaultMode = ParallelFaultMode.AggregateAll
    }
};

await eventMediator.PublishAsync(new OrderPlacedEvent(orderId), settings, cancellationToken);
```

| API | Role |
| --- | --- |
| `CommandMediationSettings.Routing` | Command routing config |
| `CommandMediationSettings.Items` | Command ambient context seed |
| `QueryMediationSettings.Routing` | Query routing config |
| `QueryMediationSettings.Items` | Query ambient context seed |
| `EventMediationSettings.Routing` | Event routing config |
| `EventMediationSettings.Execution` | Event execution concurrency/fault mode |
| `EventMediationSettings.ThrowIfNoHandlerFound` | Event no-handler behavior |
| `EventMediationSettings.AutoRegisterUnregisteredMessageTypes` | On-the-spot registration for plain events |

## Packages

- `LiteBus.Commands.Abstractions`
- `LiteBus.Queries.Abstractions`
- `LiteBus.Events.Abstractions`
- `LiteBus.Messaging.Abstractions`

## Requires

- `mediator.handler-filtering`
- `mediator.execution-context`
- `mediator.events` (for execution settings)

## Invariants

- Default routing accepts all handlers (`Tags` empty and predicate returns true).
- Items flow into `AmbientExecutionContext.Current.Items` for the mediation call.
- `ThrowIfNoHandlerFound` only applies to event publish.
- `AutoRegisterUnregisteredMessageTypes` only applies to event publish requests.

## Non-Goals

- Global default policy store across all mediator calls.
- Persistence of settings beyond one mediator invocation.
- Runtime mutation of module registration.

## Observability

No settings-specific telemetry surfaces are emitted. Settings influence behavior but do not emit dedicated metrics.

Operational alternatives:
- Log effective settings at call boundaries in application code.
- Use test coverage to validate policy combinations for critical paths.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `mediating_event_with_no_handlers_should_throw_exception_when_ThrowIfNoHandlerFound_is_true` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_no_handlers_should_not_throw_exception_when_ThrowIfNoHandlerFound_is_false` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_items_in_settings_propagates_context_correctly` | `LiteBus.Mediator.UnitTests` |
| `mediating_an_command_with_specified_tag_goes_through_handlers_with_that_tag_and_handlers_without_any_tag_correctly` | `LiteBus.Mediator.UnitTests` |
| `Mediating_StreamQuery_PassesMetadataViaExecutionContext` | `LiteBus.Mediator.UnitTests` |

### Untested

- Event `AutoRegisterUnregisteredMessageTypes` combined with large-scale polymorphic dispatch.
- Cross-cutting settings policy helpers in external libraries.

### Out-of-Scope

- Long-lived session-level routing profiles.
- Broker routing configuration.

## Deep Docs

- [Command module](../../concepts/commands.md)
- [Query module](../../concepts/queries.md)
- [Event module](../../concepts/events.md)
