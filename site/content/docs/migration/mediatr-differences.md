# LiteBus vs. MediatR

LiteBus and MediatR are both in-process mediators for .NET, but they make different trade-offs. This page is for a developer deciding between them, or one who knows MediatR and wants to map concepts onto LiteBus quickly. It states where the two agree, where they differ, and how to translate the common patterns.

Both libraries route a message to its handler so callers depend on an interface rather than a concrete service. The difference is in how much structure the library imposes and how much control it gives over the handler pipeline.

## The Core Split

MediatR has two message shapes: a request (`IRequest<T>`), handled by one handler, and a notification (`INotification`), handled by many. Whether a request reads or writes is a convention you keep in your head.

LiteBus splits the write-side request into a command and the read-side request into a query, and gives each its own mediator and handler interfaces. The type a message implements states its intent: `ICommand<T>` changes state, `IQuery<T>` reads it, `IEvent` announces a fact. A reviewer sees the category from the declaration without opening the handler.

| MediatR | LiteBus | Notes |
| --- | --- | --- |
| `IRequest` (no result) | `ICommand` | One handler, returns `Task` |
| `IRequest<T>` (write) | `ICommand<T>` | One handler, returns `Task<T>` |
| `IRequest<T>` (read) | `IQuery<T>` | One handler, side-effect free by convention |
| `IStreamRequest<T>` | `IStreamQuery<T>` | One handler, returns `IAsyncEnumerable<T>` |
| `INotification` | `IEvent` or any POCO | Zero to many handlers; LiteBus does not require an interface |
| `IRequestHandler<TReq, TRes>` | `ICommandHandler<TCmd, TRes>` / `IQueryHandler<TQry, TRes>` | Method is `HandleAsync` |
| `INotificationHandler<T>` | `IEventHandler<T>` | Method is `HandleAsync` |

## Pipeline Model

MediatR wraps a request in `IPipelineBehavior<TRequest, TResponse>` delegates. A behavior calls `next()` to continue, and code before and after that call runs before and after the handler. Cross-cutting steps are written as behaviors and apply to every request unless you constrain the generic.

LiteBus does not use a wrapping delegate. Each message type has named stages: pre-handlers, the main handler, post-handlers, and error-handlers. You attach a step to the stage where it belongs rather than reconstructing the stage from where you call `next()`. A pre-handler always runs before the main handler; a post-handler always runs after a successful one; an error-handler runs only on exception.

| Concern | MediatR | LiteBus |
| --- | --- | --- |
| Run before handler | Code before `next()` in a behavior | `ICommandPreHandler<T>` / `IQueryPreHandler<T>` |
| Run after handler | Code after `next()` in a behavior | `ICommandPostHandler<T, TResult>` / `IQueryPostHandler<T, TResult>` |
| Handle failure | `try/catch` around `next()` in a behavior | `ICommandErrorHandler<T>` |
| Short-circuit | Skip calling `next()` | Return an answer from a shortcut, a denial from a guard, or failures from a validator, which the pipeline reports as distinct outcomes |
| Cross-cutting for all messages | Open generic behavior | Open generic pre/post/error handler, or a handler on a base interface (polymorphic dispatch) |

The pipeline is documented in full at [The Handler Pipeline](../concepts/handler-pipeline.md).

## Translating a Behavior to LiteBus

A MediatR logging behavior:

```csharp
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        Console.WriteLine($"Handling {typeof(TRequest).Name}");
        var response = await next();
        Console.WriteLine($"Handled {typeof(TRequest).Name}");
        return response;
    }
}
```

The equivalent in LiteBus is two open generic handlers, each owning one side of the call:

```csharp
public sealed class CommandLoggingPre<TCommand> : ICommandPreHandler<TCommand> where TCommand : ICommand
{
    public Task PreHandleAsync(TCommand command, CancellationToken ct = default)
    {
        Console.WriteLine($"Handling {typeof(TCommand).Name}");
        return Task.CompletedTask;
    }
}

public sealed class CommandLoggingPost<TCommand> : ICommandPostHandler<TCommand> where TCommand : ICommand
{
    public Task PostHandleAsync(TCommand command, object? result, CancellationToken ct = default)
    {
        Console.WriteLine($"Handled {typeof(TCommand).Name}");
        return Task.CompletedTask;
    }
}
```

The cost is two types instead of one. The benefit is that "before" and "after" are not coupled through a shared local variable, and the post-handler does not run when the main handler throws, which is usually what you want for logging success.

## Capability Differences

| Capability | LiteBus | MediatR |
| --- | --- | --- |
| DI container | Container-agnostic core; adapters for Microsoft DI and Autofac | Registers into Microsoft DI; broad container support via that |
| Handler resolution | Reflection runs once at registration into a cached `MessageRegistry`; mediation reads metadata | Resolves through `IServiceProvider` at request time |
| Event concurrency | Per-call: priority groups and handlers within a group run `Sequential` or `Parallel` independently | A single notification publisher strategy |
| Handler selection | Built-in `[HandlerTag]` and runtime `HandlerPredicate` filtering | Write a behavior to filter |
| Polymorphic dispatch | A handler for a base type or interface runs for derived messages (pre/post/error and events) | A handler for `Base` does not run for `Derived` |
| Durable messaging | Built-in command inbox and event outbox with PostgreSQL stores | Not built in; use an external library |
| License | MIT, free | Check the current MediatR license terms for your use |

## Choosing

Choose LiteBus when the message category (command, query, event) should be visible in the type system, when you want named pipeline stages instead of wrapping behaviors, when event concurrency or handler filtering matters, or when you want a durable inbox or outbox without adding another dependency.

Choose MediatR when you prefer one request abstraction over the command/query split, when your pipeline needs are met by behaviors, or when you depend on its ecosystem of community packages.

The two are not mutually exclusive at the concept level: the CQS discipline LiteBus enforces is a discipline you can also keep by hand in MediatR. LiteBus moves it from convention into the compiler.

## Next
