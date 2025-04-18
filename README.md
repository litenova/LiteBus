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

- **Built for .NET 8** - Designed to leverage modern C# and .NET capabilities
- **Zero external dependencies** - Completely standalone with no third-party dependencies
- **Reduced reflection usage** - Optimized for performance with minimal reflection
- **DDD-friendly design** - Support for plain domain events without framework dependencies, keeping your domain model clean
- **Comprehensive messaging types**:
  - `ICommand` / `ICommand<TResult>` - For state-changing operations
  - `IQuery<TResult>` - For data retrieval operations
  - `IStreamQuery<TResult>` - For streaming large datasets via `IAsyncEnumerable<T>`
  - `IEvent` - For notifications and event-driven architecture
  - Support for POCO objects as messages without framework interfaces

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
// Define a command with a result
public class CreateProductCommand : ICommand<ProductDto>
{
    public required string Name { get; init; }
    public required decimal Price { get; init; }
    public string? Description { get; init; }
}

// Implement a command handler
public class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, ProductDto>
{
    private readonly IProductRepository _repository;
    
    public CreateProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<ProductDto> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = command.Name,
            Price = command.Price,
            Description = command.Description
        };
        
        await _repository.SaveAsync(product, cancellationToken);
        
        return new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            Description = product.Description
        };
    }
}

// Configure in ASP.NET Core
services.AddLiteBus(liteBus =>
{
    liteBus.AddCommandModule(module =>
    {
        module.RegisterFromAssembly(typeof(Program).Assembly);
    });
    
    liteBus.AddQueryModule(module =>
    {
        module.RegisterFromAssembly(typeof(Program).Assembly);
    });
    
    liteBus.AddEventModule(module =>
    {
        module.RegisterFromAssembly(typeof(Program).Assembly);
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
    public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductCommand command)
    {
        var product = await _commandMediator.SendAsync(command);
        return Ok(product);
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