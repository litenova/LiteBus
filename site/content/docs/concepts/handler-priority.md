# Handler Priority

`[HandlerPriority]` controls the order handlers run within a single stage of the pipeline. Use it when one pre-handler must run before another, or when event handlers need to run in phases. This page assumes you know the four pipeline stages from [The Handler Pipeline](handler-pipeline.md); priority orders handlers inside each stage, it does not reorder the stages themselves.

## What Handler Priority Controls

Handler Priority defines the sequence in which handlers of the same type (e.g., pre-handlers, post-handlers, or event handlers) are executed.

- **Lower numbers have higher priority** (i.e., they execute first).
- The default priority for any handler without the attribute is `0`.
- Handlers with the same priority value are not guaranteed to execute in a specific order relative to each other (unless configured for sequential execution in the Event Module).

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

## The Reserved Framework Band

LiteBus ships pipeline handlers of its own, such as the audit record writer registered by `EnableAuditing()`. Those handlers need a documented position so that ordering against them is a guarantee rather than something each application rediscovers by experiment.

`LiteBusHandlerPriority` names the band:

| Constant | Value | Used by |
| --- | --- | --- |
| `Default` | `0` | Any handler with no `[HandlerPriority]` |
| `FrameworkFloor` | `1_000_000` | The lowest value reserved for LiteBus |
| `Persistence` | `FrameworkFloor + 100` | LiteBus handlers that persist state |
| `Observability` | `FrameworkFloor + 200` | LiteBus handlers that observe and record |

Keep application handlers below `FrameworkFloor`. Everything at or above it may be reordered between LiteBus releases. Because handlers run in ascending order and an unannotated handler sits at zero, your handlers run before LiteBus's by default.

To run *after* LiteBus writes its audit record, give your handler a priority above `LiteBusHandlerPriority.Observability`:

```csharp
[HandlerPriority(LiteBusHandlerPriority.Observability + 1)]
public sealed class AfterAuditing : ICommandCompletionHandler
{
    // ...
}
```

## Best Practices

1.  **Use for Determinism**: Only apply priority when a specific execution order is required for correctness (e.g., validation before action).
2.  **Keep Gaps**: Leave gaps between priority numbers (e.g., 10, 20, 30) to make it easier to insert new handlers in the future without re-numbering.
3.  **Use Constants**: Define priority levels as constants in a shared class to improve readability and avoid magic numbers.

```csharp
public static class HandlerPriorities
{
    public const int Validation = 10;
    public const int Enrichment = 20;
    public const int Auditing = 100;
}
```

## Next
