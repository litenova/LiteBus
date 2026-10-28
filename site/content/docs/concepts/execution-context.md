# Execution Context

The execution context is the shared state for a single mediation call. Pre-handlers, the main handler, and post-handlers use it to pass data to each other and to read the cancellation token and tags. They can also abort the pipeline and override the result, all without changing the message contract. This page is for a developer coordinating handlers within one `SendAsync`, `QueryAsync`, or `PublishAsync` call.

Read [The Handler Pipeline](handler-pipeline.md) first. The context is what the stages described there share.

## Execution Context Contents and Lifetime

The context holds metadata about the current mediation. It carries a cancellation token, a string-keyed `Items` bag, a type-keyed `Data` store, the mediation tags, the handled instant, and the result slot. It is created when a message enters a mediator and lives until that mediation completes.

LiteBus stores it in an `AsyncLocal<IExecutionContext>`, so it stays ambient and flows across `async`/`await` boundaries within one logical call.

### Accessing the Current Context

Take `IExecutionContext` as a constructor dependency. It is registered as a scoped service, so resolving it returns the context of the current mediation. The dependency appears in the handler's type signature, where a reader and a unit test can both see it:

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

Scoped means per mediation here, not per request. The mediator creates one dispatch scope per mediation from the root scope factory, so the dispatch scope is a sibling of the ambient HTTP request scope rather than a child of it. A scoped `DbContext` in a handler is not the request's, and two `SendAsync` calls in one request get two different scoped instances. State that belongs to the message belongs in `Data`, not in a scoped service.

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

### Typed Data

`Data` is an `IHandleContextData`, a store keyed by the CLR type of the value rather than by a string. Use it whenever one stage resolves something a later stage needs.

`Data` exists for a guard whose decision depends on loaded state. Authorization that must read an aggregate cannot move out of the handler unless the handler can reuse what the guard loaded. Without `Data`, putting the check in a guard would load the same aggregate twice for every message.

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

`Data` offers four methods:

| Member | Behavior |
| --- | --- |
| `Set<T>(value)` | Stores the value under `T`, replacing any value already there. |
| `Get<T>()` | Returns the value, or throws `HandleContextDataNotFoundException` naming `T`. |
| `TryGet<T>(out value)` | Returns `false` instead of throwing when the value is absent. |
| `Contains<T>()`, `Remove<T>()` | Presence check and removal. |

Use `Get<T>` where an earlier stage must have supplied the value, so a missing value fails as the wiring error it is. Use `TryGet<T>` where the value is genuinely optional. A guard that can deny is the first case. If the guard allowed the message, the value is there.

The unkeyed slot holds one value per type, so wrap a primitive in a named type instead of storing a bare `string` that two unrelated stages could both write. Store under a base type or interface by naming the type parameter explicitly: `Data.Set<IOccurrence>(occurrence)`.

Where one mediation legitimately holds several values of one type, pass a key. A command naming two accounts stores each under its own identifier and the handler reads each back by the identifier it already has:

```csharp
// Guard
context.Data.Set(command.DebitAccountId, debit);
context.Data.Set(command.CreditAccountId, credit);

// Handler
var debit = context.Data.Get<Account>(command.DebitAccountId);
```

Keys are compared with `object.Equals`, so an identifier value object works as a key directly. A keyed entry and the unkeyed entry of the same type are separate slots. A stage that stores unkeyed cannot erase a keyed entry by accident, and a keyed read against an unkeyed value reports the key it could not find.

Access is synchronised, so event handlers running in parallel over one context can read and write safely. Two handlers that race to set the same type still leave the value that was written last.

### Items Dictionary

The `Items` dictionary is a key-value collection (`IDictionary<string, object>`) for sharing state between handlers in the same pipeline. Use it when the key comes from outside the process, or when the value is a flag rather than an object. Prefer `Data` for anything with a type worth keying on. Both ends invent the string key independently, the cast is unchecked, and a rename in one stage becomes a runtime failure in another.

Event handlers running in parallel over one mediation may read and write `Items` concurrently, and may push audit scope state concurrently. Two handlers that race to set the same key leave the value that was written last.

Seed `Items` per call through the mediation settings object. `CommandMediationSettings.Items`, `QueryMediationSettings.Items`, and `EventMediationSettings.Items` are copied into the context when the mediation starts, so every stage sees the seeded values:

```csharp
var settings = new CommandMediationSettings
{
    Items = { ["TenantId"] = tenantId }
};

await commandMediator.SendAsync(command, settings, cancellationToken);
```

The context holds a copy. Writes a handler makes to `Items` during the mediation do not flow back into the settings object.

The following example passes a user ID from a pre-handler to a post-handler for auditing.

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

### Handled At

`HandledAt` is the instant the mediation started, read once from the `TimeProvider` the messaging module uses when the context is created. Every stage of one mediation observes the same instant, so a timestamp written by a guard and a duration measured by a post-handler share one clock reading. Advancing the clock mid-mediation does not move `HandledAt`. A nested mediation creates its own context and reads its own instant.

```csharp
var started = AmbientExecutionContext.Current.HandledAt;
```

The default clock is `TimeProvider.System`. Register a test clock with `UseTimeProvider` to pin the instant:

```csharp
registry.AddMessaging(messaging =>
{
    messaging.UseTimeProvider(fakeClock);
});
```

### Suppressing Post-Handlers

`SuppressPostHandlers()` stops the post-handlers that have not run yet. Use it when the work turned out to be a no-op and the reactions to it should not fire.

```csharp
if (_ledger.AlreadyProcessed(message.PaymentId))
{
    AmbientExecutionContext.Current.SuppressPostHandlers();
    return Task.CompletedTask;
}
```

It does not stop the calling handler, and it does not change the outcome. The mediation still reports `MediationOutcome.Succeeded`, because the main handler ran.

To stop the pipeline **before** the work happens, implement a guard such as `ICommandGuard<TCommand>` and return a `Verdict`, or a shortcut such as `IQueryShortcut<TQuery, TResult>` and return a `Shortcut<TResult>`. Both are return values rather than context calls, so the compiler requires the decision and later stages cannot run by accident. See [The Handler Pipeline](handler-pipeline.md).

### Overriding the Result

The `MessageResult` property (`object? MessageResult { get; set; }`) on `IExecutionContext` serves two purposes:

#### Main Handler Result

The main handler's return value reaches the caller directly, so most handlers never touch this property. A shortcut supplies its result through the answer it returns rather than through `MessageResult`, so the compiler checks the value at the point of the decision.

#### Post-Handler Result Replacement

Post-handler methods return `Task`, which gives a post-handler no path to return a new result value to the caller. A post-handler can instead write a replacement result directly to `MessageResult`. After all post-handlers in the chain have executed, the mediator reads this property and, if it is non-null, returns it to the caller in place of the main handler's original result.

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

> Three details apply:
> - The mediator reads `MessageResult` once, after the final post-handler on the normal (non-aborted) flow. A non-null value replaces the main handler's result no matter which stage wrote it, so a pre-handler or main-handler write takes effect the same way a post-handler write does.
> - **Last write wins**: if multiple post-handlers write to `MessageResult`, the value present after the final post-handler executes is the one returned.
> - This feature applies to commands with results (`ICommand<TResult>`) and queries (`IQuery<TResult>`). It does **not** apply to void commands (`ICommand`) or events. Writing to `MessageResult` in those pipelines is silently ignored.

For a complete worked example, see [Overriding the Result from a Post-Handler](../getting-started/cookbook.md#recipe-overriding-the-result-from-a-post-handler) in the Cookbook.

## Best Practices

1.  **Prefer `Data` Over `Items`**: Key on the type when there is a type. Where you do use `Items`, define the keys as `const string` in a shared class so a typo fails at compile time rather than missing silently at runtime.
2.  **Scope**: The execution context is scoped to a single mediation call. It is not shared across different `SendAsync` or `PublishAsync` calls.
3.  **Avoid Overuse**: The context is for cross-cutting concerns. Core business data should always be part of the message contract itself.

## Next

Read [Handler Priority](handler-priority.md) to order handlers within a stage, then [Handler Filtering](handler-filtering.md) to select handlers per call using the tags you read from the context.
