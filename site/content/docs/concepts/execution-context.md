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

### 2. Aborting Execution

You can terminate the message pipeline at any point by calling `Abort()`. This is commonly used in pre-handlers for validation or caching.

#### Aborting Without a Result

When `Abort()` is called, LiteBus throws a `LiteBusExecutionAbortedException` internally, which stops the pipeline. No further handlers (main or post) will be executed, and error-handlers do not run either, because an abort is a clean stop rather than a failure. Completion handlers still run, with `MessageOutcome.Aborted`.

```csharp
public class PermissionPreHandler : ICommandPreHandler<DeleteProductCommand>
{
    public Task PreHandleAsync(DeleteProductCommand command, CancellationToken cancellationToken = default)
    {
        if (!CurrentUserHasPermission())
        {
            // Stop processing immediately
            AmbientExecutionContext.Current.Abort();
        }
        return Task.CompletedTask;
    }
}
```

#### Recording Why the Pipeline Was Aborted

An abort reaches neither post-handlers nor error-handlers, so without a reason a short-circuited mediation leaves no trace of its cause anywhere. Pass one:

```csharp
AmbientExecutionContext.Current.Abort(messageResult: null, reason: "caller is not a member of this tenant");
```

The reason is carried on `LiteBusExecutionAbortedException.Reason` and reaches completion handlers as `MessageCompletionContext.AbortReason`. It is what lets an audit trail record a refusal rather than silently recording nothing. See [The Handler Pipeline](handler-pipeline.md) for the completion stage.

#### Aborting with a Result

If the message expects a result (e.g., `IQuery<TResult>`), you must provide a result when aborting from a pre-handler. This pattern implements caching: a pre-handler returns a cached value and skips the main handler.

```csharp
public class CachingPreHandler : IQueryPreHandler<GetProductByIdQuery>
{
    public Task PreHandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(query.ProductId, out ProductDto cachedProduct))
        {
            // Abort the pipeline and provide the cached value as the result
            AmbientExecutionContext.Current.Abort(cachedProduct);
        }
        return Task.CompletedTask;
    }
}
```

### 3. Accessing Tags

The `Tags` collection contains the tags that were specified when the message was mediated. This allows handlers to dynamically change their behavior based on the context.

```csharp
public class ProductQueryHandler : IQueryHandler<GetProductQuery, ProductDto>
{
    public Task<ProductDto> HandleAsync(GetProductQuery query, CancellationToken cancellationToken = default)
    {
        var tags = AmbientExecutionContext.Current.Tags;

        if (tags.Contains("IncludeExtraDetails"))
        {
            // Fetch and return a more detailed DTO
        }
        else
        {
            // Return a standard DTO
        }
    }
}
```

### 4. Cancellation Token

The `CancellationToken` for the operation is also available on the execution context, which is the same token passed to the handler methods.

### 5. `MessageResult`: Aborting and Post-Handler Override

The `MessageResult` property (`object? MessageResult { get; set; }`) on `IExecutionContext` serves two distinct purposes:

#### Purpose 1: Carrying the Result Out of an Aborted Pipeline

When you call `executionContext.Abort(result)` from a pre-handler, LiteBus stores the supplied value in `MessageResult` and then terminates the pipeline. This is how the mediator knows what value to return when execution is aborted before the main handler runs.

```csharp
public class CachingPreHandler : IQueryPreHandler<GetProductByIdQuery>
{
    public Task PreHandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(query.ProductId, out ProductDto cachedProduct))
        {
            // Abort writes cachedProduct to MessageResult, then throws internally.
            AmbientExecutionContext.Current.Abort(cachedProduct);
        }
        return Task.CompletedTask;
    }
}
```

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
