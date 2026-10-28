# LiteBus vs. MediatR

LiteBus and MediatR both route messages to handlers so callers depend on an interface rather than a concrete service. LiteBus is MIT licensed, open source, free for commercial use, and will remain free. MediatR 13.0.0 and later ships under a commercial license with a free Community License below USD 5 million annual gross revenue. Earlier versions remain Apache 2.0 or MIT (see [MediatR licensing](https://mediatr.io/), retrieved 2026-10-09). The two libraries agree on mediation and differ on how much structure the type system carries and how much control the caller has over the handler pipeline.

> **This page is for** a developer deciding between LiteBus and MediatR, or one who knows MediatR and wants to map concepts onto LiteBus quickly.
>
> **After reading it** you can translate requests, notifications, and behaviors to commands, queries, events, and pipeline stages. [Getting Started](../getting-started/README.md) runs each LiteBus shape in code.

## The Core Split

MediatR has two message shapes: a request (`IRequest<T>`), handled by one handler, and a notification (`INotification`), handled by many. Whether a request reads or writes is a convention with no representation in the type system.

```csharp
public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<ProductDto>;

public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto>;

public sealed record ProductCreatedEvent(Guid ProductId);
```

LiteBus splits requests into commands for writes and queries for reads, and gives each side its own mediator and handler interfaces. The type a message implements states its intent: `ICommand<T>` changes state, `IQuery<T>` reads it, and `IEvent` announces a fact. A reviewer sees the category from the declaration without opening the handler.

| MediatR | LiteBus | Notes |
| --- | --- | --- |
| `IRequest` (no result) | `ICommand` | One handler, returns `Task` |
| `IRequest<T>` (write) | `ICommand<T>` | One handler, returns `Task<T>` |
| `IRequest<T>` (read) | `IQuery<T>` | One handler, side-effect free by convention |
| `IStreamRequest<T>` | `IStreamQuery<T>` | One handler, returns `IAsyncEnumerable<T>` |
| `INotification` | `IEvent` or any POCO | Zero to many handlers. LiteBus does not require an interface |
| `IRequestHandler<TReq, TRes>` | `ICommandHandler<TCmd, TRes>` / `IQueryHandler<TQry, TRes>` | Method is `HandleAsync` |
| `INotificationHandler<T>` | `IEventHandler<T>` | Method is `HandleAsync` |

## Pipeline Model

MediatR wraps a request in `IPipelineBehavior<TRequest, TResponse>` delegates. A behavior calls `next()` to continue, and code before and after that call runs before and after the handler. Cross-cutting steps are written as behaviors and apply to every request unless you constrain the behavior's generic type parameters.

LiteBus does not use a wrapping delegate. Each message type has named stages: pre-handlers, the main handler, post-handlers, and error-handlers. You attach each step to the stage where it belongs instead of placing code around a `next()` call. A pre-handler always runs before the main handler. A post-handler always runs after a successful one. An error-handler runs only on exception.

| Concern | MediatR | LiteBus |
| --- | --- | --- |
| Run before handler | Code before `next()` in a behavior | `ICommandPreHandler<T>` / `IQueryPreHandler<T>` |
| Run after handler | Code after `next()` in a behavior | `ICommandPostHandler<T, TResult>` / `IQueryPostHandler<T, TResult>` |
| Handle failure | `try/catch` around `next()` in a behavior | `ICommandErrorHandler<T>` |
| Short-circuit | Skip calling `next()` | A shortcut answers, a guard denies, or a validator fails. The pipeline reports each as a distinct outcome |
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

The LiteBus equivalent is two open generic handlers, one for each side of the call:

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

The cost is two types instead of one. The benefit is that "before" and "after" are not coupled through a shared local variable. The post-handler does not run when the main handler throws.

## Capability Differences

| Capability | LiteBus | MediatR |
| --- | --- | --- |
| DI container | Container-agnostic core, with adapters for Microsoft DI and Autofac | Registers with Microsoft DI, with support for other containers through it |
| Handler resolution | Reflection runs once at registration and fills a cached `MessageRegistry`, which mediation then reads | Resolves through `IServiceProvider` at request time |
| Event concurrency | Each call sets priority groups and handlers within a group to `Sequential` or `Parallel` independently | A single notification publisher strategy |
| Handler selection | Built-in `[HandlerTag]` and runtime `HandlerPredicate` filtering | Write a behavior to filter |
| Polymorphic dispatch | A handler for a base type or interface runs for derived messages (pre/post/error and events) | A handler for `Base` does not run for `Derived` |
| Durable messaging | Built-in command inbox and event outbox with PostgreSQL stores | Not built in. Use an external library |
| License | MIT licensed, open source, free for commercial use, and will remain free | Check the current MediatR license terms for your use |

## Choosing

Choose LiteBus when any of these hold:

- The message category (command, query, event) should be visible in the type system.
- You want named pipeline stages instead of wrapping behaviors.
- Event concurrency or handler filtering matters.
- You want a durable inbox or outbox without adding another dependency.

Choose MediatR when any of these hold:

- You prefer one request abstraction over the command/query split.
- Your pipeline needs are met by behaviors.
- You depend on its ecosystem of community packages.

The two are not mutually exclusive at the concept level. You can follow by hand in MediatR the CQS discipline that LiteBus enforces. LiteBus moves that discipline from convention into the compiler.

## Migrating from MediatR

### Handler Registration

MediatR:

```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PlaceOrderHandler).Assembly));
```

LiteBus:

```csharp
services.AddLiteBus(builder =>
{
    builder.AddMessaging(messaging =>
        messaging.Contracts.Register<OrderPlaced>("orders.events.placed", 1));
    builder.AddCommands(c => c.RegisterFromAssembly(typeof(PlaceOrderHandler).Assembly));
    builder.AddEvents(e => e.RegisterFromAssembly(typeof(PlaceOrderHandler).Assembly));
});
```

Call `RegisterFromAssembly` once per assembly on each semantic module builder (`AddCommands`, `AddQueries`, `AddEvents`). Each scan registers the message types and handlers for its axis. Register durable contracts for any type stored in inbox or outbox.

`IMediator.Send` maps to `ICommandMediator.SendAsync` or `IQueryMediator.QueryAsync`, and `IMediator.Publish` maps to `IEventMediator.PublishAsync`.

### Pipeline Behaviors

Register pre-handlers and post-handlers as ordinary DI types discovered by assembly scanning. See [Pipeline Model](#pipeline-model) for the full behavior mapping.

| MediatR behavior concern | LiteBus approach |
| --- | --- |
| Validation before handler | `ICommandValidator<TCommand>` (returns `Validity`, so a failure is a return value, not an exception) |
| Query read-only guard | Analyzer LB1003 warns when query handlers inject command, event, inbox, or outbox APIs |

### Commands with Results

MediatR uses one `IRequest<TResponse>` abstraction. LiteBus keeps writes and reads separate:

- `ICommand` for fire-and-forget mutations
- `ICommand<TResult>` when the handler returns a value
- `IQuery<TResult>` for reads

Do not store `ICommand<TResult>` in the inbox. Analyzer LB1004 reports that mistake.

### Notifications and Events

Replace `INotification` with `IEvent` and `INotificationHandler<T>` with `IEventHandler<T>`. Publish through `IEventMediator.PublishAsync`. For integration events that must survive process restarts, enqueue through `IOutbox.EnqueueAsync` and let the outbox processor publish after commit.

### Durable Messaging Patterns

MediatR has no inbox or outbox. Common LiteBus replacements:

| MediatR pattern | LiteBus replacement |
| --- | --- |
| Retry failed handler in memory | Inbox processor with lease, retry, and dead-letter storage |
| Publish after `SaveChanges` | EF Core transactional outbox plus `ITransactionalOutbox` |
| Defer work to background thread | `MessageVisibility` on `InboxAcceptMetadata` (for example `Visibility`) |
| Idempotent webhook handling | `IInbox.AcceptAsync` with `InboxAcceptMetadata.Idempotency` |

### Hosting and Operations

- Register background processors with `EnableInboxProcessor` / `EnableOutboxProcessor` on the module builder.
- Map operator APIs with `AddLiteBusManagementEndpoints` from `LiteBus.Extensions.AspNetCore`.
- Export metrics with `AddLiteBusInboxMetrics()`, `AddLiteBusOutboxMetrics()`, and `AddLiteBusTransportMetrics()` from the matching `*.Extensions.OpenTelemetry` packages you reference.

See [Hosted services](../architecture/hosted-services.md) for startup tasks, processor options, and management endpoint recipes.

## Next

- [Getting Started](../getting-started/README.md) to run each LiteBus shape in code
- [The Handler Pipeline](../concepts/handler-pipeline.md) for stage order and decisions
- [Migration](README.md) for version upgrade procedures
