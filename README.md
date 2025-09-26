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
</p>

<p align="center">
  For detailed documentation and examples, please visit the <strong><a href="https://github.com/litenova/LiteBus/wiki">Wiki</a></strong>.
</p>

LiteBus is an in-process mediator designed from the ground up for modern .NET. It helps you implement **Command Query Separation (CQS)** and **Domain-Driven Design (DDD)** patterns by providing a clean, decoupled architecture for your application's business logic.


## Why Choose LiteBus?

*   **CQS & DDD First-Class Citizens:** Enforces clean architecture with distinct, semantic contracts like `ICommand<TResult>`, `IQuery<TResult>`, and `IEvent`. You can even publish pure POCO domain events without coupling your model to the framework.

*   **Optimized for High Performance:** Minimizes runtime overhead by discovering and caching handler metadata at startup. Handlers are resolved lazily from your DI container, and large datasets are handled efficiently with `IAsyncEnumerable<T>` streaming via `IStreamQuery<T>`.

*   **Granular Pipeline Customization:** Go beyond simple behaviors with a full pipeline of pre-handlers, post-handlers, and error handlers. Filter handlers by context using `[HandlerTag]` attributes and dynamic predicates.

*   **Advanced Event Concurrency:** Take full control over your event processing. Configure `Sequential` or `Parallel` execution for both priority groups and for handlers within the same group, allowing you to fine-tune your application's throughput and determinism.

*   **DI-Agnostic & Resilient:** Decoupled from any specific DI container, with first-class support for Microsoft DI and Autofac. It also includes a built-in **Durable Command Inbox** for guaranteed, at-least-once execution of critical commands.

## Quick Example

Here’s how to define and handle a command to create a new product.

#### 1. Define the Command

A command is a simple object representing a request. This one returns the `Guid` of the new product.

```csharp
public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<Guid>;
```

#### 2. Implement the Handler

The handler contains the business logic to process the command.

```csharp
public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _repository;

    public CreateProductCommandHandler(IProductRepository repository) => _repository = repository;

    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var product = new Product(command.Name, command.Price);
        await _repository.AddAsync(product, cancellationToken);
        return product.Id;
    }
}
```

#### 3. Configure and Use

Register LiteBus in `Program.cs` and inject `ICommandMediator` to send your command.

```csharp
// In Program.cs
builder.Services.AddLiteBus(liteBus =>
{
    // This registers the Command Module and scans the assembly for handlers.
    // The core MessageModule is included automatically.
    liteBus.AddCommandModule(module =>
    {
        module.RegisterFromAssembly(typeof(Program).Assembly);
    });
});

// In your API Controller
[ApiController]
public class ProductsController : ControllerBase
{
    private readonly ICommandMediator _commandMediator;

    public ProductsController(ICommandMediator commandMediator) => _commandMediator = commandMediator;

    [HttpPost]
    public async Task<IActionResult> Create(CreateProductCommand command)
    {
        var productId = await _commandMediator.SendAsync(command);
        return CreatedAtAction(nameof(GetById), new { id = productId }, productId);
    }
}
```

## Installation

The recommended way to get started is by installing the extension package for your DI container and the modules you need.

#### For Microsoft Dependency Injection

```shell
dotnet add package LiteBus.Commands.Extensions.Microsoft.DependencyInjection
dotnet add package LiteBus.Queries.Extensions.Microsoft.DependencyInjection
dotnet add package LiteBus.Events.Extensions.Microsoft.DependencyInjection
```

## Documentation

For comprehensive guides, advanced features, and best practices, please visit the **[LiteBus Wiki](https://github.com/litenova/LiteBus/wiki)**.

Key pages include:
- **[Getting Started](https://github.com/litenova/LiteBus/wiki/Getting-Started)**: A detailed walkthrough for new users.
- **[v4.0 Migration Guide](https://github.com/litenova/LiteBus/wiki/Migration-Guide-v4)**: A critical guide for upgrading from v3.
- **[Advanced Concepts](https://github.com/litenova/LiteBus/wiki/Advanced-Concepts)**: Learn about the Execution Context, Handler Filtering, and more.
- **[Durable Command Inbox](https://github.com/litenova/LiteBus/wiki/Durable-Command-Inbox)**: A guide to guaranteed command processing.

## Available Packages

The LiteBus ecosystem is split into several packages so you can install only what you need.

#### Core Modules & Abstractions
| Package | Version |
| :--- | :--- |
| `LiteBus.Commands` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Commands.svg)](https://www.nuget.org/packages/LiteBus.Commands/) |
| `LiteBus.Commands.Abstractions` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Commands.Abstractions.svg)](https://www.nuget.org/packages/LiteBus.Commands.Abstractions/) |
| `LiteBus.Queries` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Queries.svg)](https://www.nuget.org/packages/LiteBus.Queries/) |
| `LiteBus.Queries.Abstractions` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Queries.Abstractions.svg)](https://www.nuget.org/packages/LiteBus.Queries.Abstractions/) |
| `LiteBus.Events` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Events.svg)](https://www.nuget.org/packages/LiteBus.Events/) |
| `LiteBus.Events.Abstractions` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Events.Abstractions.svg)](https://www.nuget.org/packages/LiteBus.Events.Abstractions/) |
| `LiteBus.Messaging` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Messaging.svg)](https://www.nuget.org/packages/LiteBus.Messaging/) |
| `LiteBus.Messaging.Abstractions` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Messaging.Abstractions.svg)](https://www.nuget.org/packages/LiteBus.Messaging.Abstractions/) |

#### Runtime & DI Extensions
| Package | Version |
| :--- | :--- |
| `LiteBus.Runtime` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Runtime.svg)](https://www.nuget.org/packages/LiteBus.Runtime/) |
| `LiteBus.Runtime.Abstractions` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Runtime.Abstractions.svg)](https://www.nuget.org/packages/LiteBus.Runtime.Abstractions/) |
| `LiteBus.Commands.Extensions.Microsoft.DependencyInjection` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Commands.Extensions.Microsoft.DependencyInjection.svg)](https://www.nuget.org/packages/LiteBus.Commands.Extensions.Microsoft.DependencyInjection/) |
| `LiteBus.Commands.Extensions.Autofac` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Commands.Extensions.Autofac.svg)](https://www.nuget.org/packages/LiteBus.Commands.Extensions.Autofac/) |
| `LiteBus.Commands.Extensions.Microsoft.Hosting` | [![NuGet version](https://img.shields.io/nuget/vpre/LiteBus.Commands.Extensions.Microsoft.Hosting.svg)](https://www.nuget.org/packages/LiteBus.Commands.Extensions.Microsoft.Hosting/) |
| *(Query and Event extensions follow the same pattern)* | |

## Contributing

Contributions are welcome! Please feel free to open an issue or submit a pull request.

## License

LiteBus is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for details.
