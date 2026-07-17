# Migrating from MediatR

LiteBus and MediatR both implement in-process message dispatch, but LiteBus separates commands, queries, and events into explicit pipelines and adds durable inbox and outbox patterns for at-least-once delivery.

## Concept Mapping

| MediatR | LiteBus |
| --- | --- |
| `IRequest` / `IRequest<TResponse>` | `ICommand` / `ICommand<TResult>` for writes; `IQuery<TResult>` for reads |
| `INotification` | `IEvent` |
| `IRequestHandler<TRequest, TResponse>` | `ICommandHandler<TCommand, TResult>` or `IQueryHandler<TQuery, TResult>` |
| `INotificationHandler<TNotification>` | `IEventHandler<TEvent>` |
| `IPipelineBehavior<TRequest, TResponse>` | `ICommandPreHandler<T>`, `ICommandPostHandler<T>`, `IQueryPreHandler<T, TResult>`, and matching post/error handlers |
| `IMediator.Send` | `ICommandMediator.SendAsync` or `IQueryMediator.QueryAsync` |
| `IMediator.Publish` | `IEventMediator.PublishAsync` |
| Handler assembly scanning | `builder.RegisterFromAssembly(assembly)` on `ILiteBusBuilder` |
| No built-in durability | `IInbox.AcceptAsync` and `IOutbox.EnqueueAsync` with PostgreSQL or EF Core storage |

## Handler Registration

MediatR:

```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(PlaceOrderHandler).Assembly));
```

LiteBus:

```csharp
services.AddLiteBus(builder =>
{
    builder.Modules.AddCommandModule(c => c.RegisterFromAssembly(typeof(PlaceOrderHandler).Assembly));
    builder.Modules.AddEventModule(e => e.RegisterFromAssembly(typeof(PlaceOrderHandler).Assembly));
    builder.Contracts.Register<OrderPlaced>("orders.events.placed", 1);
});
```

`RegisterFromAssembly` registers message types and handlers across message, command, query, and event LiteBus. Register durable contracts for any type stored in inbox or outbox.

## Pipeline Behaviors

MediatR `IPipelineBehavior` wraps the main handler in one ordered chain. LiteBus runs pre-handlers, the main handler, then post-handlers per pipeline (command, query, or event). Register pre/post handlers as ordinary DI types discovered by assembly scanning.

| MediatR behavior concern | LiteBus approach |
| --- | --- |
| Validation before handler | `ICommandPreHandler<TCommand>` |
| Logging or metrics after handler | `ICommandPostHandler<TCommand>` |
| Exception handling | `ICommandErrorHandler<TCommand>` |
| Query read-only guard | Analyzer LB1003 warns when query handlers inject command, event, inbox, or outbox APIs |

## Commands with Results

MediatR uses one `IRequest<TResponse>` abstraction. LiteBus keeps writes and reads separate:

- `ICommand` for fire-and-forget mutations
- `ICommand<TResult>` when the handler returns a value
- `IQuery<TResult>` for reads

Do not store `ICommand<TResult>` in the inbox. Analyzer LB1004 reports that mistake.

## Notifications and Events

Replace `INotification` with `IEvent` and `INotificationHandler<T>` with `IEventHandler<T>`. Publish through `IEventMediator.PublishAsync`. For integration events that must survive process restarts, enqueue through `IOutbox.EnqueueAsync` and let the outbox processor publish after commit.

## Durable Messaging Patterns

MediatR has no inbox or outbox. Common LiteBus replacements:

| MediatR pattern | LiteBus replacement |
| --- | --- |
| Retry failed handler in memory | Inbox processor with lease, retry, and dead-letter storage |
| Publish after `SaveChanges` | EF Core transactional outbox plus `ITransactionalOutbox` |
| Defer work to background thread | `MessageVisibility` on `InboxAcceptMetadata` (for example `VisibleAfter`) |
| Idempotent webhook handling | `IInbox.AcceptAsync` with `InboxAcceptMetadata.Idempotency` |

## Hosting and Operations

- Register background processors with `EnableInboxProcessor` / `EnableOutboxProcessor` on the module builder.
- Map operator APIs with `AddLiteBusManagementEndpoints` from `LiteBus.Extensions.AspNetCore`.
- Export metrics with `AddLiteBusInboxMetrics()`, `AddLiteBusOutboxMetrics()`, and `AddLiteBusTransportMetrics()` from the matching `*.Extensions.OpenTelemetry` packages you reference.

See [Hosted services](../architecture/hosted-services.md) for startup tasks, processor options, and management endpoint recipes.
