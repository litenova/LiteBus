# Polymorphic Dispatch

Polymorphic dispatch lets one handler process a whole family of related messages by targeting a base type or interface they share. Write an auditing pre-handler once against `IAuditableCommand` and it runs for every command that implements it. This page builds on the handler stages in [The Handler Pipeline](handler-pipeline.md) and pairs with [Open Generic Handlers](open-generic-handlers.md), which solves the related but distinct problem of applying behavior to all messages regardless of shape.

## What Polymorphic Dispatch Does

Polymorphic dispatch uses C# contravariance (`in` keyword) in handler interfaces. It lets you write common, cross-cutting logic once and apply it to an entire family of commands, queries, or events.

**Important**: This feature applies to **pre-handlers, post-handlers, and error handlers**. It does not apply to main handlers, as they are resolved to the most specific message type so each message has a single, definitive handler.

## How It Works

Consider a set of commands that all relate to auditable actions.

### 1. Define a Base Message Type

First, define a base class or interface that all related messages will implement.

```csharp
// Base interface for all auditable commands
public interface IAuditableCommand : ICommand
{
    Guid CorrelationId { get; }
    string UserId { get; }
}

// Concrete commands that implement the base interface
public sealed class CreateProductCommand : IAuditableCommand, ICommand<Guid>
{
    // ... properties
}

public sealed class DeleteUserCommand : IAuditableCommand, ICommand
{
    // ... properties
}
```

### 2. Create a Handler for the Base Type

Now, create a single pre-handler that targets the base `IAuditableCommand` interface.

```csharp
// This single pre-handler will run for ANY command implementing IAuditableCommand.
public sealed class AuditingPreHandler : ICommandPreHandler<IAuditableCommand>
{
    private readonly IAuditLogger _auditLogger;

    public AuditingPreHandler(IAuditLogger auditLogger)
    {
        _auditLogger = auditLogger;
    }

    public Task PreHandleAsync(IAuditableCommand command, CancellationToken cancellationToken = default)
    {
        // Log that an auditable action is about to occur.
        _auditLogger.Log(
            $"Audit: Action of type '{command.GetType().Name}' initiated by user '{command.UserId}'."
        );
        return Task.CompletedTask;
    }
}
```

### 3. Mediation

When you send a derived command, LiteBus automatically discovers and executes the handler for the base type.

```csharp
// Sending a CreateProductCommand...
await _commandMediator.SendAsync(new CreateProductCommand { ... });
// ...will trigger AuditingPreHandler.

// Sending a DeleteUserCommand...
await _commandMediator.SendAsync(new DeleteUserCommand { ... });
// ...will also trigger AuditingPreHandler.
```

## Technical Explanation

This behavior is enabled by the contravariant type parameter (`in TMessage`) on the handler interfaces:

```csharp
// The 'in' keyword allows a handler for a base type to accept a derived type.
public interface IMessagePreHandler<in TMessage> { ... }
public interface IMessagePostHandler<in TMessage, in TMessageResult> { ... }
```

When LiteBus resolves handlers, its `ActualTypeOrFirstAssignableTypeMessageResolveStrategy` finds handlers for both the concrete message type and any of its base types or implemented interfaces.

### Message Descriptor Resolution Precedence

Main-handler mediation resolves a **message descriptor** before handlers are loaded from that descriptor. The strategy applies the following order:

1. **Exact registration**: when the runtime message type (or its generic type definition for constructed generics) is registered, that descriptor wins immediately.
2. **Most-derived assignable match**: otherwise the strategy scans registered message types assignable from the runtime type and picks the candidate with the highest inheritance depth score (base-type chain length plus implemented interface count).
3. **Ambiguity guard**: when two or more candidates share the same depth score, mediation throws `AmbiguousMessageResolveException` instead of picking arbitrarily.

Indirect pipeline handlers (pre, post, and error) still link from every assignable registration on the resolved descriptor. Register base-interface handlers against the interface type; LiteBus routes them through the indirect collections described in [The Handler Pipeline](handler-pipeline.md).

## Use Cases

1.  **Auditing**: Create a single post-handler for an `IAuditable` interface to log all state-changing operations.
2.  **Authorization**: Implement a single pre-handler for a `ISecuredOperation` interface to check user permissions for a family of related commands or queries.
3.  **Validation**: A pre-handler for a base `IPaginatedQuery` could validate that pagination parameters (`PageNumber`, `PageSize`) are within valid ranges for all queries that support pagination.
4.  **Tenant Isolation**: A pre-handler for an `ITenantSpecific` interface can scope the request to the correct tenant.

## Best Practices

1.  **Define Clear Base Contracts**: The base message interface or class should define the common data needed by the polymorphic handler.
2.  **Use for Cross-Cutting Concerns**: Polymorphic dispatch is ideal for logic that applies uniformly across a set of related messages, such as security, logging, or validation.
3.  **Remember the Scope**: This feature is for pre-handlers, post-handlers, and error handlers. Each concrete command, query, or event must still have its own specific main handler.

## Polymorphic Dispatch vs. Open Generic Handlers

Both features let you write "write once, apply many" handlers, but they solve fundamentally different problems. Understanding when to use each, or both together, is key to a clean architecture.

### Comparison Table

| Aspect | Polymorphic Dispatch | Open Generic Handlers |
| :--- | :--- | :--- |
| **Mechanism** | C# contravariance (`in TMessage`). A handler for a base type accepts derived types. | Generic type closing. LiteBus calls `MakeGenericType` at startup to create concrete handlers. |
| **Targeting** | A specific base type or interface (e.g., `IAuditableCommand`). | All messages satisfying generic constraints (e.g., `where T : ICommand`). |
| **Message requirements** | Each message must _explicitly_ implement the base interface. | No changes to messages required. Constraints are checked automatically. |
| **Handler receives** | The _base type_ (e.g., `IAuditableCommand`). Can access shared properties directly. | The _concrete type_ (e.g., `CreateProductCommand`). No access to shared properties unless the handler casts or uses reflection. |
| **Scope** | Opt-in: only the subset of messages implementing the interface. | Opt-out: applies to _all_ matching messages by default. |
| **Granularity** | Fine-grained: you control exactly which messages are affected by choosing which implement the interface. | Broad: everything matching the constraint is included unless you add narrower constraints. |
| **DI registration** | Standard: `module.Register<AuditingPreHandler>()`. | Must use open generic syntax: `module.Register(typeof(MyHandler<>))`. |
| **Runtime cost** | Zero extra cost: standard assignability check during handler resolution. | Zero extra cost: closed handlers are cached at startup, identical to hand-written ones. |
| **Best for** | Behavior that _depends on shared data_ from the base interface. | Behavior that applies _universally_ and doesn't need shared properties. |

### When to Use Polymorphic Dispatch

Use polymorphic dispatch when your handler **needs to read or write properties defined on a shared interface**. The handler receives the message typed as the base interface, giving direct access to those shared members.

```csharp
// The handler needs CorrelationId and UserId; polymorphic dispatch is ideal.
public interface IAuditableCommand : ICommand
{
    Guid CorrelationId { get; }
    string UserId { get; }
}

public sealed class AuditingPreHandler : ICommandPreHandler<IAuditableCommand>
{
    public Task PreHandleAsync(IAuditableCommand command, CancellationToken ct)
    {
        // Direct access to shared properties; no casting needed.
        _logger.Log($"User {command.UserId} initiated {command.GetType().Name} (Correlation: {command.CorrelationId})");
        return Task.CompletedTask;
    }
}
```

**Good fit for:**
- Auditing with `CorrelationId` / `UserId`
- Authorization by reading `ISecuredOperation.RequiredPermission`
- Tenant isolation using `ITenantSpecific.TenantId`
- Pagination validation on `IPaginatedQuery.PageSize`

### When to Use Open Generic Handlers

Use open generic handlers when the behavior **does not depend on the message's shape** and should apply to all (or most) messages of a given type.

```csharp
// The handler only needs the type name; no shared interface needed.
public sealed class CommandLoggingPreHandler<T> : ICommandPreHandler<T>
    where T : ICommand
{
    public Task PreHandleAsync(T message, CancellationToken ct)
    {
        // Works for every command, regardless of its properties.
        _logger.LogInformation("Executing: {Type}", typeof(T).Name);
        return Task.CompletedTask;
    }
}
```

**Good fit for:**
- Logging / tracing every command or query
- Performance metrics and timing
- Transaction wrapping (`UnitOfWork` commit/rollback)
- FluentValidation integration (resolve `IValidator<T>` from DI)
- Idempotency checks (record/check a command ID)

### Decision Flowchart

Ask yourself these questions in order:

1. **Does the handler need shared properties from the message?**
   - **Yes**: Use **Polymorphic Dispatch**. Define a base interface with those properties.
   - **No**: Continue to 2.

2. **Should the handler apply to _all_ messages of this module, or only a specific subset?**
   - **All** (or most): Use an **Open Generic Handler** with a broad constraint (e.g., `where T : ICommand`).
   - **A specific subset**: Use an **Open Generic Handler** with a narrow constraint (e.g., `where T : ICommand, IAuditable`) _or_ use **Polymorphic Dispatch** on the subset interface.

3. **Do I want explicit opt-in or automatic opt-in?**
   - **Explicit** (each message must declare participation): Use **Polymorphic Dispatch**.
   - **Automatic** (applies unless constraints exclude it): Use **Open Generic Handler**.

### Using Both Together

The two features complement each other. You can use open generic handlers for universal concerns and polymorphic dispatch for targeted behavior, all on the same message.

```csharp
// --- Universal: applies to ALL commands (open generic) ---
public sealed class CommandLogger<T> : ICommandPreHandler<T> where T : ICommand
{
    public Task PreHandleAsync(T message, CancellationToken ct)
    {
        Console.WriteLine($"[LOG] Executing: {typeof(T).Name}");
        return Task.CompletedTask;
    }
}

// --- Targeted: applies only to auditable commands (polymorphic dispatch) ---
public sealed class AuditTrailWriter : ICommandPostHandler<IAuditableCommand>
{
    public Task PostHandleAsync(IAuditableCommand command, object? result, CancellationToken ct)
    {
        // Uses shared properties from the interface
        _auditStore.Write(command.UserId, command.CorrelationId, command.GetType().Name);
        return Task.CompletedTask;
    }
}

// --- A message that participates in both ---
public sealed class CreateProductCommand : IAuditableCommand, ICommand<Guid>
{
    public Guid CorrelationId { get; init; }
    public string UserId { get; init; }
    public required string Name { get; init; }
}
```

When `CreateProductCommand` is sent, the pipeline executes:
```
1. CommandLogger<CreateProductCommand>   (open generic, universal)
2. CreateProductCommandHandler           (main handler)
3. AuditTrailWriter                      (polymorphic dispatch; reads UserId/CorrelationId)
```

See [Open Generic Handlers](open-generic-handlers.md) for the full open generics documentation.

## Next
