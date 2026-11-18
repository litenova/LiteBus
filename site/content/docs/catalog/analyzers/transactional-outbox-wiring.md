# Transactional Outbox Wiring

A command handler runs inside a unit of work that ends at the commit. Three rules keep outbox writes inside that unit of work: `LB1013` requires a `DbContext` next to transactional outbox storage, `LB1025` requires command handlers to inject the transactional outbox instead of the committing one, and `LB1026` requires every injected transactional outbox context to be configured by a `UseDbContext<TContext>()` call.

## Header

- **ID**: `analyzers.transactional-outbox-dbcontext`
- **Diagnostics**: `LB1013` (Warning), `LB1025` (Warning), `LB1026` (Warning)
- **Maturity**: GA
- **Summary**: Reports outbox wiring that separates the outbox write from the domain write.

## Trigger Conditions

### LB1013: transactional outbox storage without a database context

`LB1013` reports when:

- A named type declares a public constructor.
- The constructor declares a parameter that implements or equals `ITransactionalOutboxStore`.
- The constructor declares no parameter whose type is the Entity Framework Core `DbContext` (`Microsoft.EntityFrameworkCore`) or inherits from it.

### LB1025: non-transactional outbox in a command handler

`LB1025` reports when:

- A class implements `ICommandHandler<TCommand>` or `ICommandHandler<TCommand, TCommandResult>`.
- A public constructor declares a parameter typed as `IOutbox` or as a type implementing it.
- The compilation references `ITransactionalOutbox<TContext>`.

Query handlers, event handlers, messaging-level handlers whose message type is a command, and classes outside command handling are out of scope. Private constructors are skipped, as in the other transactional-constructor rules.

### LB1026: transactional outbox context not configured

`LB1026` runs at compilation end. It collects the type arguments of every `UseDbContext<TContext>()` call on the Entity Framework Core inbox or outbox storage builder, and every public constructor parameter typed as `ITransactionalOutbox<TContext>`. It reports each injected context that no collected call configures. The rule reports nothing when the compilation holds no `UseDbContext<TContext>()` call on a storage builder, because the configuring call may live in another assembly.

## Bad Example

### LB1013

```csharp
using LiteBus.Outbox.Abstractions;

public sealed class OrderService
{
    public OrderService(ITransactionalOutboxStore outbox)
    {
    }
}
```

Expected diagnostic:

- `LB1013` on the constructor declaration.

### LB1025

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Outbox.Abstractions;

public sealed record PlaceOrderCommand(int OrderId) : ICommand;

public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand>
{
    public PlaceOrderCommandHandler(IOutbox outbox)
    {
    }

    public Task HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

Expected diagnostic:

- `LB1025` on the `outbox` parameter, naming the handler.

### LB1026

```csharp
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

public sealed class OrderService
{
    public OrderService(ITransactionalOutbox<BillingDbContext> outbox)
    {
    }
}

public static class OutboxStorageConfiguration
{
    public static void Configure(EfCoreOutboxStorageModuleBuilder builder)
    {
        builder.UseDbContext<OrdersDbContext>();
    }
}
```

Expected diagnostic:

- `LB1026` on the `outbox` parameter, naming `OrderService` and `BillingDbContext`.

## Good Example

### LB1013

```csharp
using LiteBus.Outbox.Abstractions;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext : DbContext
{
}

public sealed class OrderService
{
    public OrderService(ITransactionalOutboxStore outbox, AppDbContext dbContext)
    {
    }
}
```

### LB1025

```csharp
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Outbox.Abstractions;

public sealed class OrdersDbContext
{
}

public sealed record PlaceOrderCommand(int OrderId) : ICommand;

public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand>
{
    public PlaceOrderCommandHandler(ITransactionalOutbox<OrdersDbContext> outbox)
    {
    }

    public Task HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
```

The handler stages the envelope in the same `DbContext` transaction as the domain write.

### LB1026

```csharp
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

public sealed class OrderService
{
    public OrderService(ITransactionalOutbox<OrdersDbContext> outbox)
    {
    }
}

public static class OutboxStorageConfiguration
{
    public static void Configure(EfCoreOutboxStorageModuleBuilder builder)
    {
        builder.UseDbContext<OrdersDbContext>();
    }
}
```

The injected context matches the context the storage builder configures.

## Suppression Guidance

### LB1013

- Keep outbox enqueue and domain persistence in the same EF unit of work.
- Suppress only for adapter layers that intentionally resolve `DbContext` through a factory or an ambient scope and cannot express it in constructor parameters.
- Include a comment describing the alternate transaction boundary.

### LB1025

- Prefer injecting `ITransactionalOutbox<TContext>` so the outbox row commits with the domain write.
- Suppress only when the handler intentionally enqueues outside the unit of work, and document why the separate commit is safe.
- Keep suppression at handler scope, not project scope.

### LB1026

- Prefer passing the injected context to `UseDbContext<TContext>()` on the storage builder.
- Suppress only when the configuring call lives in another assembly that this compilation cannot see.
- Keep suppression at the injection site, not project scope.

## Test Coverage

Sources: `tests/LiteBus.Analyzers.UnitTests/TransactionalOutboxWithoutDbContextAnalyzerTests.cs`, `tests/LiteBus.Analyzers.UnitTests/NonTransactionalOutboxInCommandHandlerAnalyzerTests.cs`, `tests/LiteBus.Analyzers.UnitTests/TransactionalOutboxContextNotConfiguredAnalyzerTests.cs`

| Test method | Suite | Verifies |
| --- | --- | --- |
| `TransactionalOutboxWithoutDbContext_ShouldReportMissingDbContext` | `TransactionalOutboxWithoutDbContextAnalyzerTests` | Missing `DbContext` alongside transactional outbox store reports `LB1013` |
| `CommandHandlerWithoutOutbox_ProducesNoDiagnostic` | `NonTransactionalOutboxInCommandHandlerAnalyzerTests` | Handler without an outbox is accepted |
| `CommandHandlerInjectingOutbox_ProducesDiagnostic` | `NonTransactionalOutboxInCommandHandlerAnalyzerTests` | `IOutbox` dependency reports `LB1025` |
| `CommandHandlerWithResultInjectingOutbox_ProducesDiagnostic` | `NonTransactionalOutboxInCommandHandlerAnalyzerTests` | `IOutbox` dependency on a result handler reports `LB1025` |
| `CommandHandlerInjectingTransactionalOutbox_ProducesNoDiagnostic` | `NonTransactionalOutboxInCommandHandlerAnalyzerTests` | Transactional outbox dependency is accepted |
| `QueryHandlerInjectingOutbox_ProducesNoDiagnostic` | `NonTransactionalOutboxInCommandHandlerAnalyzerTests` | Query handlers are out of scope |
| `EventHandlerInjectingOutbox_ProducesNoDiagnostic` | `NonTransactionalOutboxInCommandHandlerAnalyzerTests` | Event handlers are out of scope |
| `MessagingHandlerForCommandInjectingOutbox_ProducesNoDiagnostic` | `NonTransactionalOutboxInCommandHandlerAnalyzerTests` | Messaging-level handlers for commands are out of scope |
| `NonHandlerClassInjectingOutbox_ProducesNoDiagnostic` | `NonTransactionalOutboxInCommandHandlerAnalyzerTests` | Classes outside command handling are out of scope |
| `CommandHandlerWithPrivateOutboxConstructor_ProducesNoDiagnostic` | `NonTransactionalOutboxInCommandHandlerAnalyzerTests` | Private constructors are out of scope |
| `Descriptor_IsWarningInOutboxCategoryAndEnabledByDefault` | `NonTransactionalOutboxInCommandHandlerAnalyzerTests` | `LB1025` is a warning in `LiteBus.Outbox`, on by default |
| `OutboxConfiguredForInjectedContext_ProducesNoDiagnostic` | `TransactionalOutboxContextNotConfiguredAnalyzerTests` | Configured context is accepted |
| `OutboxConfiguredForDifferentContext_ProducesDiagnostic` | `TransactionalOutboxContextNotConfiguredAnalyzerTests` | Unconfigured context reports `LB1026` |
| `NoUseDbContextCall_ProducesNoDiagnostic` | `TransactionalOutboxContextNotConfiguredAnalyzerTests` | No storage call in the compilation reports nothing |
| `InboxBuilderConfiguresContext_ProducesNoDiagnostic` | `TransactionalOutboxContextNotConfiguredAnalyzerTests` | Inbox storage builder calls count |
| `UseDbContextOnOtherType_ProducesNoDiagnostic` | `TransactionalOutboxContextNotConfiguredAnalyzerTests` | Same-named calls on other types do not count |
| `MultipleUnconfiguredContexts_ProduceDiagnostics` | `TransactionalOutboxContextNotConfiguredAnalyzerTests` | Each unconfigured context reports `LB1026` |
| `NonHandlerClassInjectingUnconfiguredContext_ProducesDiagnostic` | `TransactionalOutboxContextNotConfiguredAnalyzerTests` | The rule applies outside command handling |
| `Descriptor_IsWarningInOutboxCategoryAndEnabledByDefault` | `TransactionalOutboxContextNotConfiguredAnalyzerTests` | `LB1026` is a warning in `LiteBus.Outbox`, on by default |

Untested for `LB1013`:

- The positive path, where the constructor includes `DbContext`.
- Types with multiple constructors where one is valid and one is invalid.
