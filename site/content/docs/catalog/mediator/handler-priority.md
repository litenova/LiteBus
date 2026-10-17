# Handler Priority

- **ID**: `mediator.handler-priority`
- **Name**: Handler priority
- **Maturity**: GA
- **Summary**: Orders handlers by explicit priority and registration sequence, with optional parallel execution for events.
- **Guide**: [Handler priority](../../concepts/handler-priority.md)

## What It Does

`[HandlerPriority(int)]` sets handler priority. Lower numbers run first. For equal priority values, registration sequence is the tie-breaker.

In command/query single-handler flows, priority affects pre and post handler ordering. In event broadcast flows, handlers are grouped by priority and executed according to:
- `PriorityGroupsConcurrencyMode`
- `HandlersWithinSamePriorityConcurrencyMode`
- `HandlerFaultMode`

## Public Surface

```csharp
[HandlerPriority(1)]
public sealed class ValidateOrderPreHandler : ICommandPreHandler<PlaceOrderCommand>
{
    public Task PreHandleAsync(PlaceOrderCommand message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

| API | Role |
| --- | --- |
| `[HandlerPriority(int)]` | Declares handler priority |
| `IHandlerDescriptor.Priority` | Resolved priority metadata |
| `EventExecutionSettings.PriorityGroupsConcurrencyMode` | Cross-group execution mode |
| `EventExecutionSettings.HandlersWithinSamePriorityConcurrencyMode` | Intra-group execution mode |
| `EventExecutionSettings.FaultMode` | Error surfacing mode for sibling handlers |

## Packages

- `LiteBus.Messaging.Abstractions`
- `LiteBus.Events.Abstractions`
- `LiteBus.Events` (strategy implementation)

## Requires

- `mediator.events`
- `mediator.handler-pipeline`
- `mediator.mediation-settings`

## Invariants

- Default priority is `0` when attribute is absent.
- Lower priority executes first in sequential group mode.
- Parallel group mode removes strict cross-priority completion ordering.
- Both fault modes wait for already-started parallel tasks. `ContinueAndAggregate` runs every handler in the group and combines the failures, while `PropagateFirst` stops the group and surfaces one failure. The mode never carries across priority groups.

## Non-Goals

- Priority scheduling across process boundaries.
- Dynamic priority reconfiguration at runtime.
- Priority-based load balancing between hosts.

## Observability

No dedicated metric for priority group duration or queue depth is emitted by mediator packages.

Operational alternatives:
- Add timing instrumentation in pre/post handlers.
- Use structured logs around event publication boundaries.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `mediating_event_with_priority_handlers_executes_in_correct_order` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_sequential_priority_groups_sequential_handlers_maintains_strict_order` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_sequential_priority_groups_parallel_handlers_executes_same_priority_concurrently` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_parallel_priority_groups_executes_all_handlers_concurrently` | `LiteBus.Mediator.UnitTests` |
| `PropagateFirst_waits_for_started_siblings_before_surfacing_one_failure` | `LiteBus.Mediator.UnitTests` |
| `ContinueAndAggregate_waits_for_started_siblings_and_surfaces_every_failure` | `LiteBus.Mediator.UnitTests` |
| `ContinueAndAggregate_runs_the_handlers_behind_a_failure` | `LiteBus.Mediator.UnitTests` |
| `ContinueAndAggregate_does_not_run_a_later_priority_group_after_a_failure` | `LiteBus.Mediator.UnitTests` |
| `Send_CommandWithMultiplePostHandlers_LastWriteWins` | `LiteBus.Mediator.UnitTests` |

### Untested

- Priority behavior with very high handler cardinality in one event.
- Priority interactions for indirect handlers with identical values across many assemblies.

### Out-of-Scope

- OS thread priority changes.
- External scheduler integration.

## Deep Docs

- [Event module](../../concepts/events.md)
- [The handler pipeline](../../concepts/handler-pipeline.md)
