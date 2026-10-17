# Getting Started

This guide takes you from an empty project to a working command, query, and event. You install the adapter packages, register the modules once, and send each message type through its mediator.

> **This page is for** developers who know C# and want a working command, query, and event.
>
> **After reading it** you can register LiteBus and send a command, a query, and an event. The [Cheat Sheet](cheat-sheet.md) keeps the same APIs on one page.

If you come from MediatR, read [LiteBus vs. MediatR](../migration/mediatr-differences.md) first. The concepts map closely but the registration and handler interfaces differ.

## 1. Install Packages

LiteBus has no opinion about your DI container. The core runtime is container-agnostic, and a thin adapter package wires it into either Microsoft.Extensions.DependencyInjection or Autofac. Install the adapter package for each module you use; it pulls in the module and the messaging core transitively, so you do not list those separately.

The `LiteBus` meta-package includes inbox and outbox core orchestration but not storage, dispatch, or AMQP transport. When you need durability, add storage packages (for example `LiteBus.Inbox.Storage.PostgreSql`), dispatch packages (for example `LiteBus.Inbox.Dispatch.InProcess`), and optionally processor hosting through `EnableInboxProcessor()` / `EnableOutboxProcessor()`. See [Reliable Messaging](../reliable-messaging/README.md).

### Microsoft.Extensions.DependencyInjection

```bash
dotnet add package LiteBus.Commands.Extensions.Microsoft.DependencyInjection
dotnet add package LiteBus.Queries.Extensions.Microsoft.DependencyInjection
dotnet add package LiteBus.Events.Extensions.Microsoft.DependencyInjection
```

Or install the convenience meta-package when you use every semantic module:

```bash
dotnet add package LiteBus.Extensions.Microsoft.DependencyInjection
```

Each semantic `*.Extensions.Microsoft.DependencyInjection` package pulls in `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection`, which defines `AddLiteBus` in the `LiteBus.Extensions.Microsoft.DependencyInjection` namespace. `LiteBus.Extensions.Microsoft.DependencyInjection` contains no source files; it bundles the four semantic Microsoft DI extension packages.

### Autofac

```bash
dotnet add package LiteBus.Commands.Extensions.Autofac
dotnet add package LiteBus.Queries.Extensions.Autofac
dotnet add package LiteBus.Events.Extensions.Autofac
```

You do not need all three modules. Install only the message types your application uses. A read-only service might take only the query module.

## 2. Register the Modules

Call `AddLiteBus` once, add messaging, then add the command, query, and event features you installed. During composition, the module registry validates each `IRequires<TModule>` dependency across the completed graph and builds dependencies before their dependents. Callback order does not change dependency resolution, and semantic modules do not register the messaging core automatically.

Use `RegisterFromAssembly` on command, query, and event module builders to discover handlers in each assembly. Call it once per assembly on the semantic module that owns those handlers. `RegisterFromAssembly` on `AddMessaging` registers every applicable construct it finds, main handlers included, plus all durable contracts in the assembly; prefer the axis scans for handlers and use the messaging scan for contracts and messaging-level pipeline roles. When both messaging and semantic modules scan the same assembly, handler dependency injection is owned by the semantic module with scoped lifetime.

`RegisterFromAssembly` scans an assembly once at startup and records every handler, pre-handler, post-handler, and error-handler it finds. This is the only reflection-heavy step; after it, mediation reads cached metadata. Point it at each assembly that contains handlers.

### Microsoft DI (`Program.cs`)

<!-- snippet-source: samples/LiteBus.Sample/Documentation/ModuleRegistrationSnippet.cs#module-registration -->
```csharp
services.AddLiteBus(liteBus =>
{
    var applicationAssembly = typeof(ProcessPaymentCommand).Assembly;

    liteBus.AddMessaging(_ =>
    {
    });

    liteBus.AddCommands(commands => commands.RegisterFromAssembly(applicationAssembly));
    liteBus.AddQueries(queries => queries.RegisterFromAssembly(applicationAssembly));
    liteBus.AddEvents(events => events.RegisterFromAssembly(applicationAssembly));
});
```

### Autofac

```csharp
var builder = new ContainerBuilder();

builder.AddLiteBus(builder =>
{
    builder.AddMessaging(_ =>
    {
    });

    builder.AddCommands(module =>
        module.RegisterFromAssembly(typeof(Program).Assembly));
});

var container = builder.Build();
```

`RegisterFromAssembly` registers handler types with `InstanceLifetime.Scoped`. A handler can therefore receive scoped dependencies such as a `DbContext` from the current dispatch scope.

### Log the Composition Summary

`LiteBusCompositionSummary` records what the host composed: message counts per axis, each open generic handler and how many messages it closed over, whether auditing is on and which trail, actor resolver, and record writer it uses, the declaration policies enforced, and the number of composition checks. Resolve it from the container after the host is built and log it once at startup:

```csharp
var summary = app.Services.GetRequiredService<LiteBusCompositionSummary>();
app.Logger.LogInformation("{Summary}", summary.ToString());
```

The summary is filled in after every module has built; resolving it during composition returns an empty summary. The open generic line is the reason to log it: a scanned-in generic handler inserts a pipeline stage into every message it fits, and here that is a number that changes when the set changes.

### Set the Clock and the Serializer

Two cross-cutting decisions live on the messaging builder. `UseTimeProvider` overrides the clock for deterministic tests; when omitted, `TimeProvider.System` is used. `UseSerializer<T>` and `UseSerializerInstance` select the `IMessageSerializer` that produces and reads persisted payloads, which is a compose-time decision because the serializer defines the wire format of every durable message. The default is `SystemTextJsonMessageSerializer` with `JsonSerializerDefaults.Web` (camel case); applications whose payloads are read by other systems state the format here rather than inherit it. See [Message Serialization](../catalog/runtime/message-serialization.md).

## 3. Send a Command

A command is an intent to change state. It is handled by exactly one handler and may return a result. Define the command as a record implementing `ICommand<TResult>`, then write the handler.

### 1. Define the Command and Result

```csharp
public sealed record ProductDto(Guid Id, string Name, decimal Price);

public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<ProductDto>;
```

### 2. Write the Handler

The handler holds the business logic. Inject whatever it needs from the container.

```csharp
public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, ProductDto>
{
    private readonly IProductRepository _repository;

    public CreateProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProductDto> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        var product = new Product(command.Name, command.Price);
        await _repository.SaveAsync(product, cancellationToken);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

### 3. Add a Validator

A validator runs before the main handler. Implement `ICommandValidator<TCommand>` and return `Validity.Invalid` for bad input; the pipeline reports it as an invalid outcome without running the handler.

```csharp
public sealed class CreateProductValidator : ICommandValidator<CreateProductCommand>
{
    public Task<Validity> ValidateAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return Task.FromResult(Validity.Invalid("Product name cannot be empty.", nameof(command.Name)));

        if (command.Price <= 0)
            return Task.FromResult(Validity.Invalid("Price must be positive.", nameof(command.Price)));

        return Task.FromResult(Validity.Valid);
    }
}
```

### 4. React After Success

A post-handler runs after the main handler returns. It receives both the command and the result, which is useful for publishing a follow-up event.

```csharp
public sealed class ProductCreationNotifier : ICommandPostHandler<CreateProductCommand, ProductDto>
{
    private readonly IEventMediator _eventMediator;

    public ProductCreationNotifier(IEventMediator eventMediator)
    {
        _eventMediator = eventMediator;
    }

    public Task PostHandleAsync(CreateProductCommand command, ProductDto? result, CancellationToken cancellationToken = default)
    {
        if (result is not null)
            return _eventMediator.PublishAsync(new ProductCreatedEvent(result.Id), cancellationToken);

        return Task.CompletedTask;
    }
}
```

### 5. Send the Command

Inject `ICommandMediator` and call `SendAsync`. LiteBus runs the validator, then the handler, then the notifier in that order. The stages run sequentially in one call; LiteBus does not open a database transaction for you, so wrap the work in your own unit of work if you need atomicity.

```csharp
[ApiController]
[Route("products")]
public class ProductsController : ControllerBase
{
    private readonly ICommandMediator _commandMediator;

    public ProductsController(ICommandMediator commandMediator)
    {
        _commandMediator = commandMediator;
    }

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(CreateProductCommand command)
    {
        var product = await _commandMediator.SendAsync(command);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }
}
```

## 4. Read With a Query

A query reads data and returns a result without changing state. It implements `IQuery<TResult>` and is handled by exactly one handler. Send it with `IQueryMediator.QueryAsync`.

```csharp
public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto>;

public sealed class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ProductDto>
{
    private readonly IProductRepository _repository;

    public GetProductByIdQueryHandler(IProductRepository repository) => _repository = repository;

    public async Task<ProductDto> HandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken = default)
    {
        var product = await _repository.GetAsync(query.Id, cancellationToken);
        return new ProductDto(product.Id, product.Name, product.Price);
    }
}
```

```csharp
var product = await _queryMediator.QueryAsync(new GetProductByIdQuery(id));
```

## 5. Publish an Event

An event announces that something happened. It can have zero, one, or many handlers, and returns nothing. Events do not need a LiteBus interface; any class or record works, which lets domain models raise events without referencing LiteBus. Publish with `IEventMediator.PublishAsync`.

```csharp
public sealed record ProductCreatedEvent(Guid ProductId);

public sealed class SendWelcomeEmailOnProductCreated : IEventHandler<ProductCreatedEvent>
{
    public Task HandleAsync(ProductCreatedEvent @event, CancellationToken cancellationToken = default)
    {
        // notify a downstream system
        return Task.CompletedTask;
    }
}
```

```csharp
await _eventMediator.PublishAsync(new ProductCreatedEvent(product.Id));
```

If no handler is registered for an event, `PublishAsync` completes without error. That is intentional: publishers should not depend on who, if anyone, is listening.

## 6. Add Durable Messaging When You Need It

Mediation alone persists nothing: a crash mid-handler loses the work. When commands or integration events must survive process failure, register nested storage and dispatch inside `AddInbox` / `AddOutbox`:

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ =>
    {
    });

    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UseInMemoryStorage();
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor();
    });

    builder.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<OrderPlaced>("orders.events.placed", 1);
        outbox.UseInMemoryStorage();
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor();
    });
});
```

Accept commands with `IInbox.AcceptAsync` and enqueue events with `IOutbox.EnqueueAsync`. See [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md). If you upgrade from LiteBus v6, follow the [Migration Guide v7](../migration/v7.md). Steps for the earlier v5 to v6 transition remain in the [v6 Migration Guide](../migration/v6.md).

When integration events must commit with domain state, use the outbox, not `PublishAsync` alone. Pick the writer API for your persistence stack:

| Stack | Writer | Guide |
| --- | --- | --- |
| EF Core | `ITransactionalOutbox<TContext>` | [Outbox EF Core storage](../integrations/outbox-ef-core-storage.md) |
| PostgreSQL (Marten, Dapper, ADO.NET) | `ITransactionalOutbox` | [Transactional messaging writes](../reliable-messaging/transactional-writes.md) |

Processor and dispatch registration stay on [Outbox](../reliable-messaging/outbox.md).

You registered LiteBus, wrote a command with a validator and a post-handler, a query, and an event with one handler. The same patterns scale: add more handlers, order them, filter them, and attach cross-cutting steps once you understand the pipeline.

LiteBus registers startup tasks, background processor loops, and diagnostic probes through the host manifest. See [Hosted services](../architecture/hosted-services.md) for `IStartupTask`, `IBackgroundService`, and `IDiagnosticCheck` registration patterns.

When you enable inbox or outbox processors, add the matching OpenTelemetry adapter packages (`LiteBus.Inbox.Extensions.OpenTelemetry`, `LiteBus.Outbox.Extensions.OpenTelemetry`). Register activity sources with `AddLiteBusInboxInstrumentation()` and `AddLiteBusOutboxInstrumentation()`. Register meters with `AddLiteBusInboxMetrics()` and `AddLiteBusOutboxMetrics()`. See [Diagnostics and Health](../operations/diagnostics-and-health.md) for registration patterns.

## Next

- [Sample Application Tour](sample.md) for a runnable application that composes every module on this page
- [Cheat Sheet](cheat-sheet.md) for the same APIs on one page
- [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) for durable messaging depth
- [The Handler Pipeline](../concepts/handler-pipeline.md) for stage order and decisions
