# Cookbook and Scenarios

This page collects recipe-style solutions to scenarios you hit when building with LiteBus: contextual handling, caching, result overriding, validation, durable wiring, and more. Each recipe is self-contained and links to the concept page behind it. Use it as a lookup once you know the basics from [Getting Started](README.md).

For atomic domain + inbox/outbox writes (EF, PostgreSQL, Marten), start with [Transactional messaging writes](../reliable-messaging/transactional-writes.md).

## Recipe: Contextual Scenario Handling

**Problem**: You need to process the same command differently based on where the request came from. For example, a command from a public API needs stricter validation than one from an internal admin tool.

**Solution**: Use handler tags to create context-specific pipelines.

### 1. Define Your Contexts

Create a static class to hold your context tags. This avoids magic strings.

```csharp
public static class RequestContexts
{
    public const string PublicApi = "PublicAPI";
    public const string AdminPortal = "AdminPortal";
}
```

### 2. Tag Your Handlers

Apply the `[HandlerTag]` attribute to handlers that should only run in a specific context.

```csharp
// This validator only runs for requests from the public API.
[HandlerTag(RequestContexts.PublicApi)]
public class StrictUserUpdateValidator : ICommandValidator<UpdateUserCommand>
{
    public Task ValidateAsync(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        // Public API cannot change a user's role.
        if (command.Role != null)
        {
            throw new ValidationException("Role cannot be changed via the public API.");
        }
        return Task.CompletedTask;
    }
}

// This handler is untagged, so it runs for ALL contexts.
public class CommonUserUpdateValidator : ICommandValidator<UpdateUserCommand>
{
    public Task ValidateAsync(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            throw new ValidationException("Email is required.");
        }
        return Task.CompletedTask;
    }
}
```

### 3. Mediate with Context

When sending the command, specify the context tag.

```csharp
// In your Public API controller:
[HttpPut("{id}")]
public async Task<IActionResult> UpdateUser(Guid id, UpdateUserCommand command)
{
    // Send the command with the "PublicAPI" context.
    // This will trigger both the StrictUserUpdateValidator and the CommonUserUpdateValidator.
    await _commandMediator.SendAsync(command, RequestContexts.PublicApi);
    return NoContent();
}

// In your Admin Portal controller:
[HttpPut("{id}")]
public async Task<IActionResult> UpdateUserFromAdmin(Guid id, UpdateUserCommand command)
{
    // Send the command with the "AdminPortal" context.
    // This will ONLY trigger the CommonUserUpdateValidator, as the strict validator is not tagged for this context.
    await _commandMediator.SendAsync(command, RequestContexts.AdminPortal);
    return NoContent();
}
```

**Result**: You have successfully implemented different validation rules for the same command without `if/else` logic in your handlers, keeping them clean and focused.

---

## Recipe: Cross-Cutting Logging with Open Generic Handlers

**Problem**: You want every command in your application to be logged before and after execution, without modifying any existing command handlers.

**Solution**: Use open generic pre- and post-handlers that automatically apply to all commands.

### 1. Define the Open Generic Handlers

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
        _logger.LogInformation("Starting command: {CommandType}", typeof(TCommand).Name);
        return Task.CompletedTask;
    }
}

public sealed class CommandLoggingPostHandler<TCommand> : ICommandPostHandler<TCommand>
    where TCommand : ICommand
{
    private readonly ILogger<CommandLoggingPostHandler<TCommand>> _logger;

    public CommandLoggingPostHandler(ILogger<CommandLoggingPostHandler<TCommand>> logger)
    {
        _logger = logger;
    }

    public Task PostHandleAsync(TCommand message, object? messageResult, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Completed command: {CommandType}", typeof(TCommand).Name);
        return Task.CompletedTask;
    }
}
```

### 2. Register

If the open generic handlers are in the same assembly you're scanning, `RegisterFromAssembly` discovers them automatically:

```csharp
builder.Services.AddLiteBus(registry =>
{
    registry.AddMessageModule(_ => { });
    registry.AddCommandModule(module =>
    {
        // Discovers both open generic and concrete handlers
        module.RegisterFromAssembly(typeof(Program).Assembly);
    });
});
```

If they're in a **different assembly** (e.g., a shared infrastructure library), register them explicitly:

```csharp
builder.Services.AddLiteBus(registry =>
{
    registry.AddMessageModule(_ => { });
    registry.AddCommandModule(module =>
    {
        // From external library
        module.Register(typeof(CommandLoggingPreHandler<>));
        module.Register(typeof(CommandLoggingPostHandler<>));

        // Scan this assembly for concrete command handlers
        module.RegisterFromAssembly(typeof(Program).Assembly);
    });
});
```

### 3. Result

Every command automatically gets logging:

```
Starting command: CreateProductCommand
Completed command: CreateProductCommand
Starting command: UpdateStockLevelCommand
Completed command: UpdateStockLevelCommand
```

No changes to existing handlers or commands are required. New commands added in the future will also be logged automatically.

---

## Recipe: Generic FluentValidation Integration

**Problem**: You have FluentValidation validators for your commands and want them to run automatically in the LiteBus pipeline without writing a pre-handler for each command.

**Solution**: Create a single open generic pre-handler that resolves the correct `IValidator<T>` from DI.

### 1. Define the Generic Validator

```csharp
public sealed class FluentValidationPreHandler<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    private readonly IEnumerable<IValidator<TCommand>> _validators;

    public FluentValidationPreHandler(IEnumerable<IValidator<TCommand>> validators)
    {
        _validators = validators;
    }

    public async Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        if (!_validators.Any()) return;

        var context = new ValidationContext<TCommand>(message);
        var results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }
}
```

### 2. Register

If the open generic validator is in the same assembly you're scanning, `RegisterFromAssembly` discovers it automatically:

```csharp
builder.Services.AddLiteBus(registry =>
{
    registry.AddMessageModule(_ => { });
    registry.AddCommandModule(module =>
    {
        module.RegisterFromAssembly(typeof(Program).Assembly); // picks up FluentValidationPreHandler<> too
    });
});

// Register your FluentValidation validators
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
```

If the handler is in a **different assembly**, register it explicitly:

```csharp
builder.Services.AddLiteBus(registry =>
{
    registry.AddMessageModule(_ => { });
    registry.AddCommandModule(module =>
    {
        module.Register(typeof(FluentValidationPreHandler<>)); // from external library
        module.RegisterFromAssembly(typeof(Program).Assembly);
    });
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
```

**Result**: Every command is validated automatically. Commands without a registered `IValidator<T>` are passed through without validation. Commands with validators are validated before the main handler runs.

---

## Recipe: Overriding the Result from a Post-Handler

**Problem**: Your post-handler needs to replace the result returned to the caller, for example to enrich a result with data from a downstream service, or to transform an immutable result object (such as a `FluentResults` `Result<T>`). Post-handler methods return `Task`, so there is no return-value path for replacing the result directly.

**Solution**: Write the replacement result to `AmbientExecutionContext.Current.MessageResult`. After all post-handlers in the pipeline complete, the mediator inspects this property. If it is non-null, the value is cast to `TMessageResult` and returned to the caller in place of the main handler's original result.

### How It Works

1. The main handler runs and produces a result.
2. Post-handlers execute in priority order.
3. **After all post-handlers complete**, the mediator checks `AmbientExecutionContext.Current.MessageResult`.
4. If the property is non-null, its value is returned to the caller instead of the main handler's result.

> **Last Write Wins**: If multiple post-handlers each write to `MessageResult`, the value present after the _final_ post-handler in the chain is the one returned. This is by design: later post-handlers have the full picture and can correct earlier ones.

> **Scope**: This feature applies to commands with results (`ICommand<TResult>`) and queries (`IQuery<TResult>`). It does not apply to void commands (`ICommand`) or events. Writing to `MessageResult` in a void command pipeline is silently ignored.

### Example

```csharp
public class EnrichResultPostHandler(IDemoService demoService)
    : ICommandPostHandler<MyCommand, Result<MyResponse>>
{
    public async Task PostHandleAsync(
        MyCommand message,
        Result<MyResponse>? messageResult,
        CancellationToken cancellationToken = default)
    {
        if (messageResult is { IsSuccess: true })
        {
            var serviceResult = await demoService.DoWork(messageResult.Value, cancellationToken);

            if (serviceResult.IsFailed)
            {
                // Write the replacement result to the execution context.
                // The mediator will return this value to the caller instead of the original result.
                AmbientExecutionContext.Current.MessageResult =
                    messageResult.WithErrors(serviceResult.Errors);
            }
        }
    }
}
```

**Result**: The caller receives the enriched or transformed result without any changes to the command contract or the main handler.

---

## Recipe: PostgreSQL Inbox, Command Dispatch, and Hosted Processor

**Problem**: A payment workflow must survive process restarts. Commands accepted at the API should run later with at-least-once delivery through in-process handlers.

**Solution**: Register storage, `UseInProcessDispatch`, and `EnableInboxProcessor` inside `AddInboxModule`. Accept commands with `IInbox.AcceptAsync`.

### 2. Wire Modules

```csharp
var dataSource = NpgsqlDataSource.Create(connectionString);

builder.Services.AddLiteBus(builder =>
{
    builder.Modules.AddMessageModule(_ => { });
    builder.Modules.AddCommandModule(c => c.RegisterFromAssembly(typeof(ProcessPaymentCommand).Assembly));

    builder.Modules.AddInboxModule(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UseProcessorOptions(new InboxProcessorOptions { BatchSize = 50 });
        inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
    });
});
```

### 3. Accept the Command

```csharp
var receipt = await inbox.AcceptAsync(
    InboxAcceptItem<ProcessPaymentCommand>.From(
        new ProcessPaymentCommand(paymentId, amount),
        InboxAcceptMetadata.Immediate with
        {
            Idempotency = new Idempotency.Keyed($"payment:{paymentId}"),
            Trace = new MessageTrace.Correlated(correlationId),
        }),
    cancellationToken);
```

---

## Recipe: PostgreSQL Outbox with AMQP Transport Dispatch

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.Modules.AddOutboxModule(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.order-submitted", 1);
        outbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        outbox.UseAmqpDispatch(
            o => o.DefaultDestination = "orders.order-submitted", new AmqpConnectionOptions
            {
                Uri = new Uri(configuration.GetConnectionString("Amqp"!)
            }));
        outbox.EnableOutboxProcessor();
    });
});
```

```csharp
await outbox.EnqueueAsync(
    OutboxEnqueueItem<OrderSubmitted>.From(
        new OrderSubmitted { OrderId = orderId },
        OutboxEnqueueMetadata.Immediate with
        {
            Target = new PublicationTarget.Topic("orders.order-submitted"),
            Trace = new MessageTrace.Correlated(correlationId),
        }),
    cancellationToken);
```

---

## Recipe: AMQP Inbox Ingress with Command Dispatch

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.Modules.AddMessageModule(_ => { });
    builder.Modules.AddCommandModule(c => c.RegisterFromAssembly(typeof(ProcessPaymentCommand).Assembly));

    builder.Modules.AddInboxModule(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        inbox.UseInProcessDispatch();
        inbox.UseAmqpIngress(ingress =>
        {
            ingress.UseOptions(new AmqpInboxIngressOptions
            {
                QueueName = "commands.inbox",
                Connection = { Uri = new Uri(configuration.GetConnectionString("Amqp")!) }
            });
        });
        inbox.EnableInboxProcessor();
    });
});
```

---

## Recipe: Transactional Outbox with PostgreSQL (Ambient Provider)

**Problem:** A command handler persists domain state (Marten, Dapper, or raw SQL) and must enqueue integration events in the same PostgreSQL transaction.

**Solution:** Share one `NpgsqlDataSource`, enable ambient transactional writers, and implement scoped `IPostgreSqlTransactionProvider`. Full walkthrough: [Transactional messaging writes](../reliable-messaging/transactional-writes.md).

### 1. Shared Data Source and LiteBus

```csharp
var dataSource = NpgsqlDataSource.Create(configuration.GetConnectionString("Orders")!);
services.AddSingleton(dataSource);
services.AddScoped<IPostgreSqlTransactionProvider, OrderUnitOfWork>();

builder.Modules.AddOutboxModule(outbox =>
{
    outbox.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
    outbox.UsePostgreSqlStorage(pg =>
    {
        pg.UseDataSource(dataSource);
        pg.EnableAmbientTransactionProvider();
    });
    outbox.UseInProcessDispatch();
    outbox.EnableOutboxProcessor();
});
```

### 2. Handler (Inject `ITransactionalOutbox`, Not `IOutbox`)

```csharp
await unitOfWork.BeginAsync(dataSource, cancellationToken);
// enlist Marten/Dapper on unitOfWork connection + transaction
await transactionalOutbox.EnqueueAsync(
    new OrderSubmittedIntegrationEvent { OrderId = order.Id },
    cancellationToken);
await unitOfWork.CommitAsync(cancellationToken);
```

**Do not** use `IOutbox` here: it auto-commits in a separate transaction.

---

## Recipe: EF Core Outbox with In-Process Event Dispatch

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.Modules.AddMessageModule(_ => { });
    builder.Modules.AddEventModule(e => e.RegisterFromAssembly(typeof(OrderSubmittedHandler).Assembly));

    builder.Modules.AddOutboxModule(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.order-submitted", 1);
        outbox.UseEntityFrameworkCoreStorage(ef => ef.UseDbContext<AppDbContext>());
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor();
    });
});
```

---

## Recipe: InMemory Inbox and Outbox for Unit Tests

```csharp
services.AddLiteBus(builder =>
{
    builder.Modules.AddMessageModule(_ => { });
    builder.Modules.AddCommandModule(c => c.Register<ProcessPaymentCommandHandler>());
    builder.Modules.AddInboxModule(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UseInMemoryStorage();
        inbox.UseInProcessDispatch();
    });

    builder.Modules.AddOutboxModule(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.order-submitted", 1);
        outbox.UseInMemoryStorage();
        outbox.UseInProcessDispatch();
    });
});
```

---

## Recipe: Full Ingress to Inbox to Handler to Outbox to AMQP

```csharp
builder.Services.AddLiteBus(builder =>
{
    var amqp = new AmqpTransportModule(new AmqpConnectionOptions
    {
        Uri = new Uri(configuration.GetConnectionString("Amqp")!)
    });

    builder.Modules.AddMessageModule(_ => { });
    builder.Modules.AddCommandModule(c => c.RegisterFromAssembly(typeof(ProcessPaymentCommandHandler).Assembly));
    builder.Modules.AddEventModule(e => e.RegisterFromAssembly(typeof(PaymentProcessedEventHandler).Assembly));

    builder.Modules.AddInboxModule(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        inbox.UseInProcessDispatch();
        inbox.UseAmqpIngress(i => i.UseOptions(new AmqpInboxIngressOptions { QueueName = "commands.inbox" }));
        inbox.EnableInboxProcessor();
    });

    builder.Modules.AddOutboxModule(outbox =>
    {
        outbox.Contracts.Register<PaymentProcessed>("payments.payment-processed", 1);
        outbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        outbox.UseAmqpDispatch(o => o.DefaultDestination = "payments.events", amqp);
        outbox.EnableOutboxProcessor();
    });
});
```

Command handlers call `IOutbox.EnqueueAsync` inside the same database transaction as domain writes.

## Next

Read [Troubleshooting](../operations/troubleshooting.md) for the exceptions these recipes can surface, then [Best Practices](best-practices.md) for conventions.
