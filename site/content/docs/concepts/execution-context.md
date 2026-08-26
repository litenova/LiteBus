# Execution Context

The execution context is the shared state for a single mediation call. It lets pre-handlers, the main handler, and post-handlers pass data to each other, read the cancellation token and tags, abort the pipeline, and override the result, all without changing the message contract. This page is for a developer coordinating handlers within one `SendAsync`, `QueryAsync`, or `PublishAsync` call. Read [The Handler Pipeline](handler-pipeline.md) first; the context is what the stages described there share.

## What the Execution Context Is

The context holds metadata about the current mediation: a cancellation token, an `Items` bag for shared state, the mediation tags, and the result slot. It is created when a message enters a mediator and lives until that mediation completes.

LiteBus stores it in an `AsyncLocal<IExecutionContext>`, so it stays ambient and flows correctly across `async`/`await` boundaries within one logical call. That is why any handler can reach it statically without it being passed as a parameter.

### Accessing the Current Context

You can access the current execution context statically from anywhere in your code via `AmbientExecutionContext.Current`.

```csharp
using LiteBus.Messaging.Abstractions;

public class MyHandler : ICommandHandler<MyCommand>
{
    public Task HandleAsync(MyCommand command, CancellationToken cancellationToken = default)
    {
        // Access the current context
        IExecutionContext context = AmbientExecutionContext.Current;

        // Use context properties
        if (context.Tags.Contains("Admin"))
        {
            // ...
        }

        return Task.CompletedTask;
    }
}
```

## Key Features

### 1. Items Dictionary

The `Items` dictionary is a key-value collection (`IDictionary<string, object>`) for sharing state between handlers in the same pipeline. This is useful for passing data discovered in a pre-handler to downstream handlers.

**Example**: Passing a User ID from a pre-handler to a post-handler for auditing.

```csharp
// Pre-handler sets the user ID
public class UserContextPreHandler : ICommandPreHandler<CreateProductCommand>
{
    public Task PreHandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserIdFromHttpContext(); // Your logic here
        AmbientExecutionContext.Current.Items["UserId"] = userId;
        return Task.CompletedTask;
    }
}

// Post-handler uses the user ID for auditing
public class AuditPostHandler : ICommandPostHandler<CreateProductCommand>
{
    public Task PostHandleAsync(CreateProductCommand command, object? result, CancellationToken cancellationToken = default)
    {
        if (AmbientExecutionContext.Current.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is string userId)
        {
            _auditLogger.Log(userId, "Created a new product.");
        }
        return Task.CompletedTask;
    }
}
```

### 2. Suppressing Post-Handlers

`SuppressPostHandlers()` stops the post-handlers that have not run yet. Use it when the work turned out to be a no-op and the reactions to it should not fire.

```csharp
if (_ledger.AlreadyProcessed(message.PaymentId))
{
    AmbientExecutionContext.Current.SuppressPostHandlers();
    return Task.CompletedTask;
}
```

It does not stop the calling handler, and it does not change the outcome: the mediation still reports `MessageOutcome.Succeeded`, because the main handler ran.

To stop the pipeline **before** the work happens, implement `ICommandShortCircuitingPreHandler<TCommand>` or `IQueryShortCircuitingPreHandler<TQuery>` and return `PipelineDirective.ShortCircuit(...)`. That is a return value rather than a context call, so the compiler requires the decision and nothing after it runs by accident. See [The Handler Pipeline](handler-pipeline.md).

### 5. `MessageResult`: Aborting and Post-Handler Override

The `MessageResult` property (`object? MessageResult { get; set; }`) on `IExecutionContext` serves two purposes:

#### Purpose 1: Carrying a Result Set by the Main Handler

The main handler's return value reaches the caller directly, so most handlers never touch this property. A short-circuiting pre-handler supplies its result through `PipelineDirective.ShortCircuit(result)` rather than through `MessageResult`, so the value is typed at the point of the decision and the pipeline can report a clear error when it does not match.

#### Purpose 2: Replacing the Result from a Post-Handler

Post-handler methods return `Task`, which provides no path to return a new result value to the caller. To work around this, a post-handler can write a replacement result directly to `MessageResult`. After all post-handlers in the chain have executed, the mediator reads this property and, if it is non-null, returns it to the caller in place of the main handler's original result.

```csharp
public class EnrichResultPostHandler : ICommandPostHandler<MyCommand, Result<MyResponse>>
{
    public async Task PostHandleAsync(
        MyCommand message,
        Result<MyResponse>? messageResult,
        CancellationToken cancellationToken = default)
    {
        if (messageResult is { IsSuccess: true })
        {
            // Replace the result with an enriched version.
            AmbientExecutionContext.Current.MessageResult =
                messageResult.WithMetadata("enriched", true);
        }
    }
}
```

> **Important nuances:**
> - Writing to `MessageResult` from a pre-handler or the main handler has **no effect** on the value returned to the caller; only the post-handler code path reads it on the normal (non-aborted) flow.
> - **Last write wins**: if multiple post-handlers write to `MessageResult`, the value present after the final post-handler executes is the one returned.
> - This feature applies to commands with results (`ICommand<TResult>`) and queries (`IQuery<TResult>`). It does **not** apply to void commands (`ICommand`) or events; writing to `MessageResult` in those pipelines is silently ignored.

For a complete worked example, see [Overriding the Result from a Post-Handler](../getting-started/cookbook.md#recipe-overriding-the-result-from-a-post-handler) in the Cookbook.

## Best Practices

1.  **Use String Constants for Keys**: To avoid typos, define the keys for the `Items` dictionary as `const string` in a shared class.
2.  **Scope**: Remember that the execution context is scoped to a single mediation call. It is not shared across different `SendAsync` or `PublishAsync` calls.
3.  **Avoid Overuse**: The context is for cross-cutting concerns. Core business data should always be part of the message contract itself.

## Next

Read [Handler Priority](handler-priority.md) to order handlers within a stage, then [Handler Filtering](handler-filtering.md) to select handlers per call using the tags you read from the context.
