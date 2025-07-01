<h1 align="center"><br>
<a href="https://github.com/litenova/LiteBus">
<img src="assets/logo/icon.png">
</a>
<br>
LiteBus
<br>
</h1>
<h4 align="center">A lightweight, flexible in-process mediator for implementing Command Query Separation (CQS)</h4>
<p align="center">
<a href="https://github.com/litenova/LiteBus/actions/workflows/release.yml">
<img src="https://github.com/litenova/LiteBus/actions/workflows/release.yml/badge.svg" alt="CI/CD Badge" />
</a>
<a href="https://codecov.io/gh/litenova/LiteBus" >
<img src="https://codecov.io/gh/litenova/LiteBus/graph/badge.svg?token=XBNYITSV5A" alt="Code Coverage Badge" />
</a>
<a href="https://www.nuget.org/packages/LiteBus">
<img src="https://img.shields.io/nuget/vpre/LiteBus.svg" alt="LiteBus Nuget Version" />
</a>
</p>
<p align="center">
For detailed documentation and examples, please visit the <a href="https://github.com/litenova/LiteBus/wiki"><b>Wiki</b></a>.
</p>

## Key Features

- **Built for .NET 8 and .NET 9** - Multi-targeting support for maximum compatibility
- **Zero external dependencies** - Completely standalone with no third-party dependencies
- **Reduced reflection usage** - Optimized for performance with minimal reflection
- **DDD-friendly design** - Support for plain domain events without library dependencies, keeping your domain model clean
- **Comprehensive messaging types**:
  - `ICommand` / `ICommand<TResult>` - For state-changing operations
  - `IQuery<TResult>` - For data retrieval operations
  - `IStreamQuery<TResult>` - For streaming large datasets via `IAsyncEnumerable<T>`
  - `IEvent` - For notifications and event-driven architecture
  - Support for POCO objects as messages without library interfaces

- **Rich handler ecosystem**:
  - Pre-handlers for validation and pre-processing
  - Post-handlers for notifications and side effects
  - Error handlers for centralized exception management
  - Support for generic handlers and messages
  - Handler ordering control
  - Contextual handler selection via tags and filters

- **Advanced features**:
  - Covariant type handling for polymorphic dispatch
  - Execution context for cross-cutting concerns
  - Aborting execution flow when needed
  - Microsoft Dependency Injection integration

## Quick Example

```csharp
// Define the command result
public record CreateProductCommandResult(Guid Id);

// Define a command with a result
public record CreateProductCommand(string Title) : ICommand<CreateProductCommandResult>;

// Implement a command validator
public class CreateProductCommandValidator : ICommandValidator<CreateProductCommand>
{
    public Task ValidateAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            throw new ValidationException("Product title cannot be empty");
            
        return Task.CompletedTask;
    }
}

// Implement a command handler
public class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, CreateProductCommandResult>
{
    private readonly IProductRepository _repository;
    
    public CreateProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<CreateProductCommandResult> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        var product = new Product(Guid.NewGuid(), command.Title);
        
        await _repository.SaveAsync(product, cancellationToken);
        
        return new CreateProductCommandResult(product.Id);
    }
}

// Configure in ASP.NET Core
services.AddLiteBus(liteBus =>
{
    liteBus.AddCommandModule(module =>
    {
        module.RegisterFromAssembly(typeof(CreateProductCommand).Assembly);
    });
});

// Use in a controller or service
public class ProductsController : ControllerBase
{
    private readonly ICommandMediator _commandMediator;
    
    public ProductsController(ICommandMediator commandMediator)
    {
        _commandMediator = commandMediator;
    }
    
    [HttpPost]
    public async Task<ActionResult<CreateProductCommandResult>> CreateProduct(CreateProductCommand command)
    {
        var result = await _commandMediator.SendAsync(command);
        return Ok(result);
    }
}
```

## Documentation

For comprehensive documentation, including detailed explanations, advanced features, and best practices, please visit the [Wiki](https://github.com/litenova/LiteBus/wiki).

## Installation

LiteBus is available as NuGet packages:

```
dotnet add package LiteBus
dotnet add package LiteBus.Extensions.MicrosoftDependencyInjection
```

Or specific modules:

```
dotnet add package LiteBus.Commands
dotnet add package LiteBus.Queries
dotnet add package LiteBus.Events
```

## License

LiteBus is licensed under the MIT License. See the LICENSE file for details.
