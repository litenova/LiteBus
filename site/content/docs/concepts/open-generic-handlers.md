# Open Generic Handlers

An open generic handler is a handler class with a type parameter, such as `CommandLoggingPreHandler<T> : ICommandPreHandler<T>`. LiteBus closes it at startup for every concrete message that satisfies its constraints, so one class becomes a pre-handler, post-handler, or error-handler for all matching messages. This is the way to add logging, validation, metrics, or transaction wrapping across a whole module without registering one handler per message. This page builds on [The Handler Pipeline](handler-pipeline.md) and pairs with [Polymorphic Dispatch](polymorphic-dispatch.md).

## When to Use This over Polymorphic Dispatch

Rule of thumb: if the handler needs properties from a shared interface, use polymorphic dispatch; if it should apply to all messages regardless of shape, use an open generic handler. The full comparison, the decision flowchart, and a combined example live on [Polymorphic Dispatch](polymorphic-dispatch.md#polymorphic-dispatch-vs-open-generic-handlers).

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
builder.Services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(module =>
    {
        module.RegisterFromAssembly(typeof(Program).Assembly);
    });
});
```

If the open generic handler lives in a different assembly, such as a shared library, register it explicitly alongside assembly scanning:

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(module =>
    {
        module.Register(typeof(CommandLoggingPreHandler<>));
        module.Register(typeof(CommandMetricsPostHandler<>));
        module.RegisterFromAssembly(typeof(Program).Assembly);
    });
});
```

## How It Executes

When you send any command, the open generic handlers are part of the pipeline:

```text
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

## Supported Type Parameter Shapes

One type parameter binds the message type. Two are closed when the handler implements a typed contract over both in order, such as `ICommandPostHandler<TCommand, TCommandResult>`, because then the second parameter binds to the result type the message declares. Any other arity throws `UnsupportedOpenGenericHandlerException` at startup: a second parameter that is the handler's own invention, such as a `TContext` or a `TStore`, has nothing to bind to. See [Troubleshooting](../operations/troubleshooting.md) for that exception.

```csharp
// Closed over each command and its declared result type.
public sealed class CommandAuditPostHandler<TCommand, TCommandResult>
    : ICommandPostHandler<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>
    where TCommandResult : notnull
{
    public Task PostHandleAsync(TCommand command, TCommandResult? result, CancellationToken cancellationToken = default)
    {
        // ...
        return Task.CompletedTask;
    }
}
```

## Common Uses

- Logging and tracing every command or query.
- Validation, including FluentValidation integration by resolving `IValidator<T>` from DI.
- Performance metrics and timing.
- Transaction wrapping with commit and rollback.
- Idempotency checks that record and verify a message ID.

## Requiring Explicit Registration

An open generic handler closed over every message it fits is the implicit behavior with the widest reach in the library: adding one file to a scanned assembly inserts a pipeline stage into every message, with no registration line for a reviewer to read. `RequireExplicitOpenGenerics` on the messaging builder makes composition fail, naming each offender, until every open generic handler is registered explicitly with `Register(typeof(MyHandler<>))`:

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.AddMessaging(messaging => messaging.RequireExplicitOpenGenerics());
    builder.AddCommands(module => module.RegisterFromAssembly(typeof(Program).Assembly));
});
```

Scanning still finds everything else. The rule is opt-in because scans have picked up open generic handlers since v4, and changing that changes what a scan is rather than fixing a defect. Whether the rule is on or not, the composition summary reports every open generic handler and how many messages it closed over; see [Getting Started](../getting-started/README.md).

## Next

Read [Generic Messages and Handlers](generic-messages-and-handlers.md) for messages that are themselves generic, then [Reliable Messaging](../reliable-messaging/README.md) to make handler work survive restarts.
