# Open Generic Handlers

An open generic handler is a handler class with a type parameter, such as `CommandLoggingPreHandler<T> : ICommandPreHandler<T>`. LiteBus closes it at startup for every concrete message that satisfies its constraints, so one class becomes a pre-handler, post-handler, or error-handler for all matching messages. This is the way to add logging, validation, metrics, or transaction wrapping across a whole module without registering one handler per message. This page builds on [The Handler Pipeline](handler-pipeline.md) and pairs with [Polymorphic Dispatch](polymorphic-dispatch.md).

## When to Use This over Polymorphic Dispatch

Both patterns let you write a handler once and apply it to many messages, but they differ in what the handler receives and which messages it covers:

| | Polymorphic Dispatch | Open Generic Handlers |
| :--- | :--- | :--- |
| Mechanism | A handler for a base type or interface accepts derived messages via contravariance (`in TMessage`). | A handler with a type parameter is closed for each concrete message at startup. |
| Message requirements | Messages must implement the shared interface. | No changes to messages; constraints are checked automatically. |
| Handler receives | The base type, with direct access to shared properties. | The concrete type, with no shared properties unless it casts. |
| Scope | Opt-in: only messages implementing the interface. | Opt-out: every message satisfying the constraint. |
| Best for | Behavior that reads shared data (`UserId`, `TenantId`, `CorrelationId`). | Universal behavior that does not need shared data (logging, metrics, transactions). |

Rule of thumb: if the handler needs properties from a shared interface, use polymorphic dispatch; if it should apply to all messages regardless of shape, use an open generic handler. The two combine, covered at the end of this page.

## How LiteBus Closes an Open Generic Handler

When you register an open generic handler such as `CommandLoggingPreHandler<>`, LiteBus:

1. Detects that the type parameter `T` in `ICommandPreHandler<T>` is an unbound generic parameter.
2. Stores the open generic handler definition.
3. For every concrete message type already registered (or registered later), checks whether the message satisfies the handler's generic constraints.
4. If it does, closes the generic (for example `CommandLoggingPreHandler<CreateProductCommand>`) and links the result into that message's pipeline.

This runs at startup, so closed handlers cost the same as hand-written ones. Registration order does not matter; the registry links open generics to messages discovered both before and after registration. The mechanics are described on [Handler Resolution Internals](../architecture/handler-resolution.md).

## Defining Open Generic Handlers

A pre-handler that logs every command:

```csharp
public sealed class CommandLoggingPreHandler<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    private readonly ILogger<CommandLoggingPreHandler<TCommand>> _logger;

    public CommandLoggingPreHandler(ILogger<CommandLoggingPreHandler<TCommand>> logger)
    {
        _logger = logger;
    }

    public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing command: {CommandType}", typeof(TCommand).Name);
        return Task.CompletedTask;
    }
}
```

A post-handler that records metrics for every command:

```csharp
public sealed class CommandMetricsPostHandler<TCommand> : ICommandPostHandler<TCommand>
    where TCommand : ICommand
{
    private readonly IMetricsService _metrics;

    public CommandMetricsPostHandler(IMetricsService metrics)
    {
        _metrics = metrics;
    }

    public Task PostHandleAsync(TCommand message, object? messageResult, CancellationToken cancellationToken = default)
    {
        _metrics.RecordCommandExecuted(typeof(TCommand).Name);
        return Task.CompletedTask;
    }
}
```

## Registration

`RegisterFromAssembly` discovers open generic handlers in the scanned assembly automatically, alongside concrete handlers:

```csharp
builder.Services.AddLiteBus(registry =>
{
    registry.AddMessaging(_ => { });
    registry.AddCommands(module =>
    {
        module.RegisterFromAssembly(typeof(Program).Assembly);
    });
});
```

If the open generic handler lives in a different assembly, such as a shared library, register it explicitly alongside assembly scanning:

```csharp
builder.Services.AddLiteBus(registry =>
{
    registry.AddMessaging(_ => { });
    registry.AddCommands(module =>
    {
        module.Register(typeof(CommandLoggingPreHandler<>));
        module.Register(typeof(CommandMetricsPostHandler<>));
        module.RegisterFromAssembly(typeof(Program).Assembly);
    });
});
```

## How It Executes

When you send any command, the open generic handlers are part of the pipeline:

```
CreateProductCommand
  1. CommandLoggingPreHandler<CreateProductCommand>   (open generic, auto-closed)
  2. CreateProductCommandHandler                       (concrete main handler)
  3. CommandMetricsPostHandler<CreateProductCommand>   (open generic, auto-closed)
```

No changes are needed to existing commands or handlers. Use `[HandlerPriority]` to order an open generic handler relative to message-specific handlers in the same stage; see [Handler Priority](handler-priority.md).

## Constraints Are Respected

Open generic handlers honor all standard C# generic constraints. A handler constrained to `where T : ICommand` does not apply to events or queries:

```csharp
// Applies to commands only.
public sealed class CommandValidator<T> : ICommandPreHandler<T>
    where T : ICommand { /* ... */ }

// Applies only to types implementing your marker interface.
public sealed class AuditHandler<T> : ICommandPostHandler<T>
    where T : class, IAuditable, new() { /* ... */ }
```

Supported constraints include interface constraints (`where T : IMyInterface`), base class constraints (`where T : MyBaseClass`), `class` and `struct` constraints, and the `new()` constraint. Use the narrowest constraint that fits, so a handler applies only where it makes sense.

## Single Type Parameter Only

Open generic handlers are supported for handlers with a single generic type parameter. Multi-parameter open generics such as `MyHandler<TCommand, TResult>` are not closed automatically; registering one throws `UnsupportedOpenGenericHandlerException` at startup. See [Troubleshooting](../operations/troubleshooting.md) for that exception.

## Common Uses

- Logging and tracing every command or query.
- Validation, including FluentValidation integration by resolving `IValidator<T>` from DI.
- Performance metrics and timing.
- Transaction wrapping with commit and rollback.
- Idempotency checks that record and verify a message ID.

## Combining with Polymorphic Dispatch

Use an open generic handler for universal concerns and polymorphic dispatch for targeted behavior on the same message:

```csharp
// Universal: applies to all commands (open generic).
public sealed class CommandLogger<T> : ICommandPreHandler<T> where T : ICommand
{
    public Task PreHandleAsync(T message, CancellationToken ct)
    {
        Console.WriteLine($"[LOG] Executing: {typeof(T).Name}");
        return Task.CompletedTask;
    }
}

// Targeted: applies only to auditable commands (polymorphic dispatch).
public sealed class AuditTrailWriter : ICommandPostHandler<IAuditableCommand>
{
    public Task PostHandleAsync(IAuditableCommand command, object? result, CancellationToken ct)
    {
        _auditStore.Write(command.UserId, command.CorrelationId, command.GetType().Name);
        return Task.CompletedTask;
    }
}

public sealed class CreateProductCommand : IAuditableCommand, ICommand<Guid>
{
    public Guid CorrelationId { get; init; }
    public string UserId { get; init; }
    public required string Name { get; init; }
}
```

Sending `CreateProductCommand` runs:

```
1. CommandLogger<CreateProductCommand>   (open generic, universal)
2. CreateProductCommandHandler           (main handler)
3. AuditTrailWriter                      (polymorphic dispatch; reads UserId/CorrelationId)
```

## Next

Read [Generic Messages and Handlers](generic-messages-and-handlers.md) for messages that are themselves generic, then [Reliable Messaging](../reliable-messaging/README.md) to make handler work survive restarts.
