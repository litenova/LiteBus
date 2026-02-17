
<h1 align="center">
  <a href="https://github.com/litenova/LiteBus">
    <img src="assets/logo/icon.png" alt="LiteBus Logo" width="128">
  </a>
  <br>
  LiteBus
</h1>

<h4 align="center">A lightweight, high-performance mediator for building clean, scalable, and testable .NET applications with CQS and DDD.</h4>

<p align="center">
  <a href="https://github.com/litenova/LiteBus/actions/workflows/release.yml">
    <img src="https://github.com/litenova/LiteBus/actions/workflows/release.yml/badge.svg" alt="Build Status" />
  </a>
  <a href="https://codecov.io/gh/litenova/LiteBus" > 
    <img src="https://codecov.io/gh/litenova/LiteBus/graph/badge.svg?token=XBNYITSV5A" alt="Code Coverage" />
  </a>
  <a href="https://www.nuget.org/packages/LiteBus.Commands.Extensions.Microsoft.DependencyInjection">
    <img src="https://img.shields.io/nuget/vpre/LiteBus.Commands.Extensions.Microsoft.DependencyInjection.svg" alt="NuGet Version" />
  </a>
  <a href="https://github.com/litenova/LiteBus/wiki">
    <img src="https://img.shields.io/badge/documentation-wiki-blue.svg" alt="Wiki" />
  </a>
</p>

<h4 align="center">LiteBus is a modern, powerful, and perpetually free alternative to MediatR.</h4>

It is, and always will be, governed by the MIT license. LiteBus helps you implement **Command Query Separation (CQS)** and **Domain-Driven Design (DDD)** patterns by providing a clean, decoupled architecture for your application's business logic.

---

## Why LiteBus?

-   **Truly Semantic:** Go beyond generic requests. With first-class contracts like `ICommand<TResult>`, `IQuery<TResult>`, and `IEvent`, your code becomes self-documenting. You can even publish clean POCOs as domain events.
-   **High Performance:** Designed for minimal overhead. Handler metadata is cached on startup, and dependencies are resolved lazily. Large datasets are handled efficiently with `IAsyncEnumerable<T>` streaming.
-   **Granular Pipeline Control:** Go beyond simple "behaviors". LiteBus provides a full pipeline with distinct, type-safe `Pre-Handlers`, `Post-Handlers`, and `Error-Handlers` for each message.
-   **Open Generic Handlers:** Write a single pre/post/error handler once and have it automatically apply to every message type matching its constraints — perfect for cross-cutting concerns like logging, validation, and metrics.
-   **Advanced Event Concurrency:** Take full control of event processing. Configure `Sequential` or `Parallel` execution for both priority groups and for handlers within the same group to fine-tune throughput.
-   **Resilient & Durable:** Guarantee at-least-once execution for critical commands with a built-in durable Command Inbox.
-   **DI-Agnostic by Design:** Decoupled from any specific DI container. First-class integration for Microsoft DI and Autofac is provided, with a simple adapter pattern to support others.

## Quick Start

### 1. Install Packages

Install the modules you need. The core messaging infrastructure is included automatically.

```bash
# For Commands
dotnet add package LiteBus.Commands.Extensions.Microsoft.DependencyInjection

# For Queries
dotnet add package LiteBus.Queries.Extensions.Microsoft.DependencyInjection

# For Events
dotnet add package LiteBus.Events.Extensions.Microsoft.DependencyInjection
```

### 2. Define Your Messages and Handlers

#### Command: Create a Product

```csharp
// The Command
public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<Guid>;

// The Handler
public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var productId = Guid.NewGuid(); // Your business logic here...
        Console.WriteLine($"Product '{command.Name}' created with ID: {productId}");
        return Task.FromResult(productId);
    }
}
```

#### Query: Get a Product by ID

```csharp
// The Query
public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductDto>;

// The DTO
public sealed record ProductDto(Guid Id, string Name, decimal Price);

// The Handler
public sealed class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ProductDto>
{
    public Task<ProductDto> HandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        // Your data retrieval logic here...
        var product = new ProductDto(query.Id, "Sample Product", 99.99m);
        return Task.FromResult(product);
    }
}
```

#### Event: A Product was Created

```csharp
// The Event (can be a simple POCO)
public sealed record ProductCreatedEvent(Guid ProductId, string Name);

// The Handler
public sealed class ProductCreatedEventHandler : IEventHandler<ProductCreatedEvent>
{
    public Task HandleAsync(ProductCreatedEvent @event, CancellationToken cancellationToken)
    {
        // Your side-effect logic here (e.g., send an email, update a projection)
        Console.WriteLine($"Handling side effects for new product '{@event.Name}'...");
        return Task.CompletedTask;
    }
}
```

### 3. Configure and Mediate

Register LiteBus and its modules in `Program.cs`, then inject the mediators into your services or controllers.

```csharp
// In Program.cs
builder.Services.AddLiteBus(liteBus =>
{
    var appAssembly = typeof(Program).Assembly;

    // Scan the assembly for all command/query/event handlers
    liteBus.AddCommandModule(module => module.RegisterFromAssembly(appAssembly));
    liteBus.AddQueryModule(module => module.RegisterFromAssembly(appAssembly));
    liteBus.AddEventModule(module => module.RegisterFromAssembly(appAssembly));
});

// In your API Controller or Service
public class ProductController : ControllerBase
{
    private readonly ICommandMediator _commandMediator;
    private readonly IQueryMediator _queryMediator;
    private readonly IEventMediator _eventMediator;

    public ProductController(ICommandMediator cmd, IQueryMediator qry, IEventMediator evt)
    {
        _commandMediator = cmd;
        _queryMediator = qry;
        _eventMediator = evt;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateProductCommand command)
    {
        // 1. Send a command to create the product
        var productId = await _commandMediator.SendAsync(command);

        // 2. Publish an event to handle side effects
        await _eventMediator.PublishAsync(new ProductCreatedEvent(productId, command.Name));

        // 3. Query for the newly created product to return it
        var productDto = await _queryMediator.QueryAsync(new GetProductByIdQuery(productId));

        return Ok(productDto);
    }
}
```

## Key Features

### A Semantic & Granular Pipeline

LiteBus provides a rich set of interfaces that make your pipeline explicit and powerful. Each message type (Command, Query, Event) has its own set of `Pre-Handlers`, `Post-Handlers`, and `Error-Handlers`.

This allows for fine-grained control, such as running validation logic, enriching a message, or logging results at specific stages of the pipeline. You can also share data between handlers via the `AmbientExecutionContext`.

```csharp
// A semantic validator that runs before the main handler

public sealed class PlaceOrderValidator : ICommandValidator<PlaceOrderCommand> // or ICommandPreHandler<PlaceOrderCommand>
{
    public Task ValidateAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.LineItems.Count == 0)
        {
            throw new ValidationException("At least one line item is required.");
        }
        return Task.CompletedTask;
    }
}

// A post-handler that runs after the command is successfully handled
public sealed class PlaceOrderNotifier : ICommandPostHandler<PlaceOrderCommand, Guid>
{
    public Task PostHandleAsync(PlaceOrderCommand command, Guid orderId, CancellationToken cancellationToken)
    {
        // Publish an OrderPlacedEvent with the result from the command handler
        return _eventPublisher.PublishAsync(new OrderPlacedEvent(orderId));
    }
}
```

The mediator also supports **polymorphic dispatch**, allowing handlers for a base message type to process any derived messages.

### Open Generic Handlers for Cross-Cutting Concerns

Write a single handler that automatically applies to **every** command, query, or event. No changes to existing messages required.

```csharp
// This pre-handler runs before EVERY command — registered once, applied everywhere
public sealed class CommandLogger<T> : ICommandPreHandler<T> where T : ICommand
{
    public Task PreHandleAsync(T message, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Executing: {typeof(T).Name}");
        return Task.CompletedTask;
    }
}

// Register the open generic type
builder.Services.AddLiteBus(liteBus =>
{
    liteBus.AddCommandModule(module =>
    {
        module.Register(typeof(CommandLogger<>));  // applies to ALL commands
        module.RegisterFromAssembly(typeof(Program).Assembly);
    });
});
```

LiteBus closes the generic at startup for each concrete message type. Generic constraints (`where T : ICommand`, `class`, `struct`, `new()`) are fully respected. Registration order does not matter.

### Advanced Eventing with Concurrency Control

Define execution priority and concurrency for event handlers to manage complex workflows.

```csharp
// This handler runs first
[HandlerPriority(1)]
public class ValidateOrderHandler : IEventHandler<OrderPlacedEvent> { /* ... */ }

// These two handlers run concurrently after the validation handler completes
[HandlerPriority(2)]
public class PersistOrderHandler : IEventHandler<OrderPlacedEvent> { /* ... */ }

[HandlerPriority(2)]
public class NotifyInventoryHandler : IEventHandler<OrderPlacedEvent> { /* ... */ }

// Configure execution strategy at runtime
await _eventMediator.PublishAsync(e, new EventMediationSettings
{
    Execution = new EventMediationExecutionSettings
    {
        // Run priority groups one after another
        PriorityGroupsConcurrencyMode = ConcurrencyMode.Sequential,
        // Run handlers within the same group in parallel
        HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel
    }
});
```

### Contextual Filtering with Tags

Execute specific handlers based on runtime context, such as the request origin.

```csharp
// This handler only runs if the "api" tag is specified
[HandlerTag("api")]
public class ApiValidationPreHandler : ICommandPreHandler<CreateProductCommand> { /* ... */ }

// Mediate with a tag
await _commandMediator.SendAsync(command, new CommandMediationSettings
{
    Filters = { Tags = ["api"] }
});
```

### Durable Command Inbox for Guaranteed Execution

Ensure critical commands are never lost by marking them for durable storage and deferred processing.

```csharp
// This command will be stored in a durable inbox and processed by a background service
[StoreInInbox]
public sealed record ProcessPaymentCommand(Guid OrderId, decimal Amount) : ICommand;
```

## Modular by Design

LiteBus is built on a modular, DI-agnostic runtime. You only install what you need.

| Category | Package Name | NuGet |
| --- | --- | --- |
| **Metapackage** | `LiteBus` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.svg)](https://www.nuget.org/packages/LiteBus/) |
| **Core Modules** | `LiteBus.Commands` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Commands.svg)](https://www.nuget.org/packages/LiteBus.Commands/) |
| | `LiteBus.Queries` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Queries.svg)](https://www.nuget.org/packages/LiteBus.Queries/) |
| | `LiteBus.Events` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Events.svg)](https://www.nuget.org/packages/LiteBus.Events/) |
| | `LiteBus.Messaging` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Messaging.svg)](https://www.nuget.org/packages/LiteBus.Messaging/) |
| | `LiteBus.Runtime` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Runtime.svg)](https://www.nuget.org/packages/LiteBus.Runtime/) |
| **Abstractions** | `LiteBus.Commands.Abstractions` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Commands.Abstractions.svg)](https://www.nuget.org/packages/LiteBus.Commands.Abstractions/) |
| | `LiteBus.Queries.Abstractions` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Queries.Abstractions.svg)](https://www.nuget.org/packages/LiteBus.Queries.Abstractions/) |
| | `LiteBus.Events.Abstractions` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Events.Abstractions.svg)](https://www.nuget.org/packages/LiteBus.Events.Abstractions/) |
| | `LiteBus.Messaging.Abstractions` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Messaging.Abstractions.svg)](https://www.nuget.org/packages/LiteBus.Messaging.Abstractions/) |
| | `LiteBus.Runtime.Abstractions` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Runtime.Abstractions.svg)](https://www.nuget.org/packages/LiteBus.Runtime.Abstractions/) |
| **MS.DI Extensions** | `LiteBus.Extensions.Microsoft.DependencyInjection` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Extensions.Microsoft.DependencyInjection.svg)](https://www.nuget.org/packages/LiteBus.Extensions.Microsoft.DependencyInjection/) |
| | `LiteBus.Commands.Extensions.Microsoft.DependencyInjection` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Commands.Extensions.Microsoft.DependencyInjection.svg)](https://www.nuget.org/packages/LiteBus.Commands.Extensions.Microsoft.DependencyInjection/) |
| | `LiteBus.Queries.Extensions.Microsoft.DependencyInjection` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Queries.Extensions.Microsoft.DependencyInjection.svg)](https://www.nuget.org/packages/LiteBus.Queries.Extensions.Microsoft.DependencyInjection/) |
| | `LiteBus.Events.Extensions.Microsoft.DependencyInjection` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Events.Extensions.Microsoft.DependencyInjection.svg)](https://www.nuget.org/packages/LiteBus.Events.Extensions.Microsoft.DependencyInjection/) |
| | `LiteBus.Messaging.Extensions.Microsoft.DependencyInjection` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Messaging.Extensions.Microsoft.DependencyInjection.svg)](https://www.nuget.org/packages/LiteBus.Messaging.Extensions.Microsoft.DependencyInjection/) |
| | `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Runtime.Extensions.Microsoft.DependencyInjection.svg)](https://www.nuget.org/packages/LiteBus.Runtime.Extensions.Microsoft.DependencyInjection/) |
| **Autofac Extensions**| `LiteBus.Commands.Extensions.Autofac` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Commands.Extensions.Autofac.svg)](https://www.nuget.org/packages/LiteBus.Commands.Extensions.Autofac/) |
| | `LiteBus.Queries.Extensions.Autofac` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Queries.Extensions.Autofac.svg)](https://www.nuget.org/packages/LiteBus.Queries.Extensions.Autofac/) |
| | `LiteBus.Events.Extensions.Autofac` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Events.Extensions.Autofac.svg)](https://www.nuget.org/packages/LiteBus.Events.Extensions.Autofac/) |
| | `LiteBus.Messaging.Extensions.Autofac` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Messaging.Extensions.Autofac.svg)](https://www.nuget.org/packages/LiteBus.Messaging.Extensions.Autofac/) |
| | `LiteBus.Runtime.Extensions.Autofac` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Runtime.Extensions.Autofac.svg)](https://www.nuget.org/packages/LiteBus.Runtime.Extensions.Autofac/) |
| **Hosting** | `LiteBus.Commands.Extensions.Microsoft.Hosting` | [![NuGet](https://img.shields.io/nuget/v/LiteBus.Commands.Extensions.Microsoft.Hosting.svg)](https://www.nuget.org/packages/LiteBus.Commands.Extensions.Microsoft.Hosting/) |

## Migrating from MediatR

LiteBus offers a more semantic and feature-rich alternative to MediatR. If you're migrating, here’s how the core concepts map.

-   **Requests (`IRequest<TResponse>` and `IRequest`)**  
    In MediatR, `IRequest` is used for both commands and queries. LiteBus separates these for CQS clarity:
    -   Use `ICommand<TResult>` for operations that change state and return a value.
    -   Use `IQuery<TResult>` for read-only operations.
    -   Use `ICommand` for fire-and-forget operations that don't return a value.

-   **Notifications (`INotification`)**  
    MediatR's `INotification` is equivalent to LiteBus's `IEvent`. A key advantage of LiteBus is that you **don't need to implement any interface**. You can publish any Plain Old C# Object (POCO) as an event, keeping your domain model completely clean.

-   **Stream Requests (`IStreamRequest<TResponse>`)**  
    This maps directly to `IStreamQuery<TResult>` in LiteBus, which returns an `IAsyncEnumerable<TResult>`. LiteBus semantically treats streams as a query concern.

-   **Pipeline Behaviors (`IPipelineBehavior<,>`)**  
    MediatR uses a generic `IPipelineBehavior` for cross-cutting concerns. LiteBus provides a more granular and type-safe pipeline with distinct stages for each message type:
    -   `ICommandPreHandler<TCommand>` / `IQueryPreHandler<TQuery>`: Run before the main handler. Ideal for validation (`ICommandValidator` is a semantic shortcut for this).
    -   `ICommandPostHandler<TCommand, TResult>` / `IQueryPostHandler<TQuery, TResult>`: Run after the main handler, with access to the result.
    -   `ICommandErrorHandler<TCommand>` / `IQueryErrorHandler<TQuery>`: Centralized error handling for specific message types.
    -   **Open generic handlers** (e.g., `MyHandler<T> : ICommandPreHandler<T> where T : ICommand`) work similarly to MediatR's open generic `IPipelineBehavior<,>` — register once and they apply to all matching message types automatically.

This granular approach eliminates the need for generic pipeline behaviors and provides a more expressive and maintainable way to build your processing pipeline.

## Our Commitment to Open Source

LiteBus was created to provide the .NET community with a modern, high-performance, and truly free open-source tool. We believe essential infrastructure libraries should be community-driven and accessible to everyone without financial barriers.

**LiteBus will always be free and licensed under the MIT license.**

We are committed to maintaining and evolving LiteBus as a community project. Contributions are welcome, and we encourage you to get involved.

## Documentation

For detailed documentation, feature guides, and examples, please visit the **[LiteBus Wiki](https://github.com/litenova/LiteBus/wiki)**.
