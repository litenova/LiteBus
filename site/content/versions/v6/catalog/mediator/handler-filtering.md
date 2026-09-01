# Handler Filtering

- **ID**: `mediator.handler-filtering`
- **Name**: Handler filtering
- **Maturity**: GA
- **Summary**: Selects participating handlers per mediation call using tags and descriptor predicates.

## What It Does

Handler filtering narrows candidate handlers before main stage resolution:
- Tag filter (`Tags`) keeps untagged handlers and tagged handlers with at least one match.
- Predicate filter (`HandlerPredicate`) applies after tag filtering.

This applies to command, query, stream query, and event mediation. In command/query flows, filtering can create zero or multiple main handlers, which then fails fast in single-handler resolution.

## Public Surface

```csharp
var settings = new CommandMediationSettings
{
    Routing = new CommandRoutingSettings
    {
        Tags = ["Admin"],
        HandlerPredicate = d => d.HandlerType.Namespace?.StartsWith("MyApp.Admin") == true
    }
};

await commandMediator.SendAsync(new ApproveOrderCommand(orderId), settings, cancellationToken);
```

| API | Role |
| --- | --- |
| `CommandRoutingSettings.Tags` | Tag filter for command mediation |
| `CommandRoutingSettings.HandlerPredicate` | Predicate filter for command mediation |
| `QueryRoutingSettings.Tags` | Tag filter for query mediation |
| `QueryRoutingSettings.HandlerPredicate` | Predicate filter for query mediation |
| `EventRoutingSettings.Tags` | Tag filter for event mediation |
| `EventRoutingSettings.HandlerPredicate` | Predicate filter for event mediation |
| `[HandlerTag("...")]` | Single static handler tag |
| `[HandlerTags("...", "...")]` | Multi-tag static handler label |

## Packages

- `LiteBus.Messaging.Abstractions`
- `LiteBus.Commands.Abstractions`
- `LiteBus.Queries.Abstractions`
- `LiteBus.Events.Abstractions`

## Requires

- `mediator.mediation-settings`
- `mediator.module-registration`

## Invariants

- Untagged handlers are always eligible when predicate returns true.
- Predicate receives `IHandlerDescriptor` and can inspect handler type, message type, priority, and tags.
- Filtering is applied before main handler uniqueness checks.

## Non-Goals

- Runtime mutation of handler tags.
- Central policy store for routing predicates.
- Cross-service tenant routing (transport axis concern).

## Observability

No dedicated filtering telemetry is emitted.

Operational alternatives:
- Emit diagnostic logs in calling layer when using custom predicates.
- Use analyzer rule `LB1011` to detect orphaned handler tags.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `mediating_an_command_with_specified_tag_goes_through_handlers_with_that_tag_and_handlers_without_any_tag_correctly` | `LiteBus.Mediator.UnitTests` |
| `mediating_an_query_with_specified_tag_goes_through_handlers_with_that_tag_and_handlers_without_any_tag_correctly` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_multiple_tags_executes_handlers_matching_any_tag` | `LiteBus.Mediator.UnitTests` |
| `Publish_Event_WithPredicate_ShouldExecuteOnlyMatchingHandlers` | `LiteBus.Mediator.UnitTests` |
| `Query_CommandHandlerPredicate_ShouldFilterHandlers` | `LiteBus.Mediator.UnitTests` |

### Untested

- Combined tags plus complex predicate in stream query flows under parallel event publishing.
- Large tag set performance characteristics.

### Out-of-Scope

- Authorization policy enforcement framework.
- Declarative rules engine for handler routing.

## Deep Docs

- [Handler filtering](../../concepts/handler-filtering.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
