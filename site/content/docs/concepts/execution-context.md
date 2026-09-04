# Execution Context

The execution context is the shared state for a single mediation call. It lets pre-handlers, the main handler, and post-handlers pass data to each other, read the cancellation token and tags, abort the pipeline, and override the result, all without changing the message contract. This page is for a developer coordinating handlers within one `SendAsync`, `QueryAsync`, or `PublishAsync` call. Read [The Handler Pipeline](handler-pipeline.md) first; the context is what the stages described there share.

## What the Execution Context Is

The context holds metadata about the current mediation: a cancellation token, a string-keyed `Items` bag, a type-keyed `Data` store, the mediation tags, and the result slot. It is created when a message enters a mediator and lives until that mediation completes.

LiteBus stores it in an `AsyncLocal<IExecutionContext>`, so it stays ambient and flows correctly across `async`/`await` boundaries within one logical call.

### Accessing the Current Context

Take `IExecutionContext` as a constructor dependency. It is registered as a scoped service, so the resolution returns the context of the mediation in flight, and the dependency appears in the handler's type signature where a reader and a unit test can both see it:

```csharp
public sealed class CancelOccurrenceCommandHandler : ICommandHandler<CancelOccurrenceCommand>
{
    private readonly IExecutionContext _context;

    public CancelOccurrenceCommandHandler(IExecutionContext context) => _context = context;

    public Task HandleAsync(CancelOccurrenceCommand command, CancellationToken cancellationToken = default)
    {
        var occurrence = _context.Data.Get<Occurrence>();
        return Task.CompletedTask;
    }
}
```

Scoped means per mediation here, which is worth being precise about because it does not mean per request. The mediator creates one dispatch scope per mediation from the root scope factory, so it is a sibling of the ambient HTTP request scope rather than a child of it: a scoped `DbContext` in a handler is not the request's, and two `SendAsync` calls in one request get two different scoped instances. State that belongs to the message belongs in `Data`, not in a scoped service.

Resolving `IExecutionContext` outside a mediation throws `NoExecutionContextException`, and a singleton must not take it.

For code that runs outside dependency injection, `AmbientExecutionContext.Current` reaches the same context statically.

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

### 1. Data: Handing a Typed Value Forward

`Data` is an `IHandleContextData`: a store keyed by the CLR type of the value rather than by a string. Use it whenever one stage resolves something a later stage needs.

The case it exists for is a guard whose decision depends on loaded state. Authorization that has to read an aggregate to decide anything cannot move out of the handler unless the handler can reuse what the guard loaded, and a second load per message is not an acceptable price for putting the check where it belongs.

```csharp
public sealed class CancelOccurrenceGuard : ICommandGuard<CancelOccurrenceCommand>
{
    private readonly IOccurrenceRepository _occurrences;
    private readonly IAuthorizer _authorizer;

    public CancelOccurrenceGuard(IOccurrenceRepository occurrences, IAuthorizer authorizer)
    {
        _occurrences = occurrences;
        _authorizer = authorizer;
    }

    public async Task<Verdict> DecideAsync(
        CancelOccurrenceCommand message,
        CancellationToken cancellationToken = default)
    {
        var occurrence = await _occurrences.LoadAsync(message.OccurrenceId, cancellationToken);

        if (occurrence is null)
        {
            return Verdict.Deny("the occurrence does not exist");
        }

        if (!await _authorizer.MayCancelAsync(occurrence, cancellationToken))
        {
            return Verdict.Deny("not permitted to cancel this occurrence");
        }

        AmbientExecutionContext.Current.Data.Set(occurrence);
        return Verdict.Allow;
    }
}

public sealed class CancelOccurrenceCommandHandler : ICommandHandler<CancelOccurrenceCommand>
{
    public Task HandleAsync(CancelOccurrenceCommand message, CancellationToken cancellationToken = default)
    {
        // The guard already loaded it. No second round trip, and no cast.
        var occurrence = AmbientExecutionContext.Current.Data.Get<Occurrence>();
        occurrence.Cancel();
        return Task.CompletedTask;
    }
}
```

The surface is four methods:

| Member | Behavior |
| --- | --- |
| `Set<T>(value)` | Stores the value under `T`, replacing any value already there. |
| `Get<T>()` | Returns the value, or throws `HandleContextDataNotFoundException` naming `T`. |
| `TryGet<T>(out value)` | Returns `false` instead of throwing when the value is absent. |
| `Contains<T>()`, `Remove<T>()` | Presence check and removal. |

Use `Get<T>` where an earlier stage is required to have supplied the value, so a missing one is a wiring error worth failing on, and `TryGet<T>` where it is genuinely optional. A guard that can deny is the first case: if the guard allowed the message, the value is there.

One value per type in the unkeyed slot, so wrap a primitive in a named type instead of storing a bare `string` that two unrelated stages will collide on. Store under a base type or interface by naming the type parameter explicitly: `Data.Set<IOccurrence>(occurrence)`.

Where one mediation legitimately holds several values of one type, pass a key. A command naming two accounts stores each under its own identifier and the handler reads each back by the identifier it already has:

```csharp
// Guard
context.Data.Set(command.DebitAccountId, debit);
context.Data.Set(command.CreditAccountId, credit);

// Handler
var debit = context.Data.Get<Account>(command.DebitAccountId);
```

Keys are compared with `object.Equals`, which is what makes an identifier value object usable directly. A keyed entry and the unkeyed entry of the same type are separate slots, so a stage storing unkeyed cannot erase a keyed one by accident, and a keyed read against an unkeyed value reports the key it could not find.

Access is synchronised, so event handlers running in parallel over one context can read and write safely. Two handlers racing to set the same type still leave whichever landed last.

### 2. Items Dictionary

The `Items` dictionary is a key-value collection (`IDictionary<string, object>`) for sharing state between handlers in the same pipeline. Reach for it when the key comes from outside the process, or when the value is a flag rather than an object. Prefer `Data` for anything with a type worth keying on: the string key is invented at both ends, the cast is unchecked, and a rename in one stage becomes a runtime failure in another.

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

### 3. Suppressing Post-Handlers

`SuppressPostHandlers()` stops the post-handlers that have not run yet. Use it when the work turned out to be a no-op and the reactions to it should not fire.

```csharp
if (_ledger.AlreadyProcessed(message.PaymentId))
{
    AmbientExecutionContext.Current.SuppressPostHandlers();
    return Task.CompletedTask;
}
```

It does not stop the calling handler, and it does not change the outcome: the mediation still reports `MediationOutcome.Succeeded`, because the main handler ran.

To stop the pipeline **before** the work happens, implement a guard such as `ICommandGuard<TCommand>` and return a `Verdict`, or a shortcut such as `IQueryShortcut<TQuery, TResult>` and return a `Shortcut<TResult>`. Both are return values rather than context calls, so the compiler requires the decision and nothing after it runs by accident. See [The Handler Pipeline](handler-pipeline.md).

### 4. `MessageResult`: Aborting and Post-Handler Override

The `MessageResult` property (`object? MessageResult { get; set; }`) on `IExecutionContext` serves two purposes:

#### Purpose 1: Carrying a Result Set by the Main Handler

The main handler's return value reaches the caller directly, so most handlers never touch this property. A shortcut supplies its result through the answer it returns rather than through `MessageResult`, so the compiler checks the value at the point of the decision.

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

1.  **Prefer `Data` Over `Items`**: Key on the type when there is a type. Where you do use `Items`, define the keys as `const string` in a shared class so a typo is a compile error rather than a silent miss.
2.  **Scope**: Remember that the execution context is scoped to a single mediation call. It is not shared across different `SendAsync` or `PublishAsync` calls.
3.  **Avoid Overuse**: The context is for cross-cutting concerns. Core business data should always be part of the message contract itself.

## Next

Read [Handler Priority](handler-priority.md) to order handlers within a stage, then [Handler Filtering](handler-filtering.md) to select handlers per call using the tags you read from the context.
