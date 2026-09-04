# Handler Priority

`[HandlerPriority]` controls the order handlers run within a single stage of the pipeline. Use it when one pre-handler must run before another, or when event handlers need to run in phases. This page assumes you know the four pipeline stages from [The Handler Pipeline](handler-pipeline.md); priority orders handlers inside each stage, it does not reorder the stages themselves.

## What Handler Priority Controls

Handler Priority defines the sequence in which handlers of the same type (e.g., pre-handlers, post-handlers, or event handlers) are executed.

- Handlers run in **ascending** priority order, so a lower number runs earlier and a higher number runs later.
- The default priority for any handler without the attribute is `0`.
- Ties break on registration order. Two handlers at the same priority run in the order their modules registered them, except in the Event Module, where the concurrency settings decide whether a priority group runs sequentially at all.

This feature replaces the `[HandlerOrder]` attribute from versions prior to v4.0.

## How to Use

Apply the `[HandlerPriority]` attribute to any handler class.

```csharp
using LiteBus.Messaging.Abstractions;

[HandlerPriority(1)]
public class MyFirstHandler : ICommandPreHandler<MyCommand>
{
    // This will execute first...
}

[HandlerPriority(10)]
public class MySecondHandler : ICommandPreHandler<MyCommand>
{
    // This will execute after MyFirstHandler...
}

public class MyDefaultPriorityHandler : ICommandPreHandler<MyCommand>
{
    // This has a default priority of 0 and will execute before MyFirstHandler.
}
```

### Use Case: Pre-Handler Ordering

Priority is commonly used to make validation run before enrichment in a pre-handler chain.

```csharp
// Priority 1: Validation runs first.
[HandlerPriority(1)]
public class ValidationPreHandler : ICommandPreHandler<CreateUserCommand>
{
    public Task PreHandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            throw new ValidationException("Email is required.");
        }
        return Task.CompletedTask;
    }
}

// Priority 2: Enrichment runs after successful validation.
[HandlerPriority(2)]
public class EnrichmentPreHandler : ICommandPreHandler<CreateUserCommand>
{
    public Task PreHandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        // Add a correlation ID to the context for other handlers to use.
        AmbientExecutionContext.Current.Items["CorrelationId"] = Guid.NewGuid();
        return Task.CompletedTask;
    }
}
```

### Use Case: Event Handler Priority Groups

For events, handlers with the same priority value form a **priority group**. The `EventMediationSettings` control how these groups execute relative to each other.

- By default, priority groups execute sequentially (Group 1 finishes before Group 2 starts).
- This allows you to create phases of event processing.

```csharp
// Priority 1: Initial data persistence.
[HandlerPriority(1)]
public class SaveToReadModelHandler : IEventHandler<OrderPlacedEvent> { /* ... */ }

// Priority 2: Notifications can only happen after data is saved.
[HandlerPriority(2)]
public class SendEmailToCustomerHandler : IEventHandler<OrderPlacedEvent> { /* ... */ }

[HandlerPriority(2)]
public class NotifyShippingDepartmentHandler : IEventHandler<OrderPlacedEvent> { /* ... */ }

// Priority 3: Analytics runs last.
[HandlerPriority(3)]
public class UpdateAnalyticsDashboardHandler : IEventHandler<OrderPlacedEvent> { /* ... */ }
```

In the example above, `SaveToReadModelHandler` completes before the two notification handlers begin when `PriorityGroupsConcurrencyMode` is `Sequential`. If priority groups run in `Parallel`, priority is grouping metadata only; the notification handlers can start before `SaveToReadModelHandler` finishes. The two notification handlers, both priority 2, form a group and run based on the `HandlersWithinSamePriorityConcurrencyMode` setting.

For more details, see the [Event Module](events.md) documentation.

## The Reserved Framework Window

LiteBus ships pipeline handlers of its own, such as the audit record writer registered by `EnableAuditing()`. Those handlers need a documented position so that ordering against them is a guarantee rather than something each application rediscovers by experiment.

`HandlerPriorities` names the window and the two application bands around it:

| Constant | Value | Used by |
| --- | --- | --- |
| `Default` | `0` | Any handler with no `[HandlerPriority]` |
| `ReservedFloor` | `1_000_000` | The lowest value reserved for LiteBus |
| `Persistence` | `ReservedFloor + 100` | LiteBus handlers that persist state |
| `Observability` | `ReservedFloor + 200` | LiteBus handlers that observe and record |
| `ReservedCeiling` | `2_000_000` | The first value above the reserved window. A boundary marker; nothing sits on it |
| `UnitOfWork` | `ReservedCeiling + 100` | An application's unit-of-work commit |

Application handlers belong below `ReservedFloor` or at or above `ReservedCeiling`. Nothing inside the window is yours: `Persistence` and `Observability` may be reordered relative to each other between releases. The floor and the ceiling are stable, so both application bands are stable.

The band from `ReservedCeiling` up to `UnitOfWork` is for application infrastructure that has to run after every LiteBus handler and still before the commit, such as a handler flushing a buffered projection the same commit will write. The two constants shared the value `2_000_000` in earlier previews, which left no such band: a handler on the ceiling tied with the commit and the order resolved by registration sequence, which is assembly scan order.

Because handlers run in ascending order and an unannotated handler sits at zero, your handlers run before LiteBus's by default. To run *after* every LiteBus handler, use the band above the ceiling:

```csharp
[HandlerPriority(HandlerPriorities.UnitOfWork)]
public sealed class CommitUnitOfWork : ICommandCompletionHandler
{
    // Runs after the audit writer, so a record the trail staged is part of this commit.
}
```

### Why There Is a Band Above the Framework

An application that needs its audit record to be atomic with the change it describes cannot commit before the record exists. The audit writer runs at `Observability`, so the commit has to run after it, which means it has to sit outside the reserved window on the far side. That is the whole reason `ReservedCeiling` exists, and it is why `UnitOfWork` is a named constant instead of advice to add one to `Observability`. It sits above the ceiling rather than on it so the ceiling stays a boundary with nothing to tie with. See [Auditing](auditing.md) for the full pattern, including what happens to a record for a failed mediation.

Priority is the only ordering rule in the completion stage. Every other role runs handlers registered for the message type before handlers registered for a base type or interface, but the completion stage merges the two and sorts by priority alone. A commit that must follow a broadly registered framework writer would otherwise be unorderable.

## Best Practices

1.  **Use for Determinism**: Only apply priority when a specific execution order is required for correctness (e.g., validation before action).
2.  **Keep Gaps**: Leave gaps between priority numbers (e.g., 10, 20, 30) to make it easier to insert new handlers in the future without re-numbering.
3.  **Use Constants**: Define priority levels as constants in a shared class to improve readability and avoid magic numbers.

Name your own bands in your own class. Do not add members to a class called `HandlerPriorities`; that name is taken by LiteBus, and the framework values have to keep meaning what they say.

```csharp
public static class AppPriorities
{
    public const int Validation = 10;
    public const int Enrichment = 20;
    public const int Notification = 100;
}
```

## Next
