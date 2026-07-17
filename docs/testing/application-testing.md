# Testing LiteBus

Handlers are plain classes with injected dependencies, so you unit test them by calling the handler method directly with mocked dependencies, and integration test the wired pipeline through a real service provider. This page covers both levels. It assumes you have read [Getting Started](../getting-started/README.md).

## Unit Testing Handlers

Handlers are simple classes with dependencies, making them easy to unit test in isolation. You do not need the LiteBus infrastructure to test a handler's logic.

### Example: Unit Testing a Command Handler

Given a command handler that creates a product:

```csharp
public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _repository;

    public CreateProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ValidationException("Product name is required.");
        }

        var product = new Product(command.Name);
        await _repository.SaveAsync(product, cancellationToken);
        return product.Id;
    }
}
```

You can test its logic using a mocking framework like Moq:

```csharp
[Fact]
public async Task CreateProductCommandHandler_WithValidName_ShouldAddProductToRepository()
{
    // Arrange
    var mockRepository = new Mock<IProductRepository>();
    var handler = new CreateProductCommandHandler(mockRepository.Object);
    var command = new CreateProductCommand { Name = "Laptop" };

    // Act
    var productId = await handler.HandleAsync(command, CancellationToken.None);

    // Assert
    Assert.NotEqual(Guid.Empty, productId);
    mockRepository.Verify(r => r.SaveAsync(It.Is<Product>(p => p.Name == "Laptop"), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task CreateProductCommandHandler_WithEmptyName_ShouldThrowValidationException()
{
    // Arrange
    var mockRepository = new Mock<IProductRepository>();
    var handler = new CreateProductCommandHandler(mockRepository.Object);
    var command = new CreateProductCommand { Name = "" };

    // Act & Assert
    await Assert.ThrowsAsync<ValidationException>(() => handler.HandleAsync(command, CancellationToken.None));
}
```

This same approach applies to pre-handlers, post-handlers, and error handlers.

## Integration Testing the Full Pipeline

To test the entire mediation pipeline, including pre-handlers, the main handler, and post-handlers, you can use an in-memory test setup.

### Example: Integration Testing a Command Pipeline

Consider a pipeline with a validator (pre-handler) and a notifier (post-handler).

```csharp
public class CommandIntegrationTests
{
    private readonly IServiceProvider _serviceProvider;

    public CommandIntegrationTests()
    {
        var services = new ServiceCollection();

        // Register mock dependencies
        services.AddSingleton<IProductRepository, InMemoryProductRepository>();
        services.AddSingleton(new Mock<IEventMediator>().Object); // Mock for the post-handler

        // Configure LiteBus with all relevant handlers
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(module =>
            {
                module.Register<CreateProductCommandValidator>(); // Pre-handler
                module.Register<CreateProductCommandHandler>();   // Main handler
                module.Register<ProductCreationNotifier>();     // Post-handler
            });
        });

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task SendAsync_WithValidCommand_ShouldExecuteFullPipeline()
    {
        // Arrange
        var commandMediator = _serviceProvider.GetRequiredService<ICommandMediator>();
        var mockEventMediator = _serviceProvider.GetRequiredService<Mock<IEventMediator>>();
        var command = new CreateProductCommand { Name = "Test Product" };

        // Act
        var productId = await commandMediator.SendAsync(command);

        // Assert
        // 1. Check the result from the main handler
        Assert.NotEqual(Guid.Empty, productId);

        // 2. Verify the repository was called (by the main handler)
        var repository = _serviceProvider.GetRequiredService<IProductRepository>();
        var product = await repository.GetByIdAsync(productId);
        Assert.NotNull(product);

        // 3. Verify the notifier was called (by the post-handler)
        mockEventMediator.Verify(p => p.PublishAsync(It.IsAny<ProductCreatedEvent>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

## Testing Code That Uses Mediators

When testing a class that depends on a mediator (e.g., a controller), you can mock the mediator interface to isolate the class from the LiteBus pipeline.

```csharp
[Fact]
public async Task ProductsController_Create_ShouldReturnCreatedResult()
{
    // Arrange
    var mockMediator = new Mock<ICommandMediator>();
    var command = new CreateProductCommand { Name = "New Gadget" };
    var expectedProductId = Guid.NewGuid();

    mockMediator
        .Setup(m => m.SendAsync(command, null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(expectedProductId);

    var controller = new ProductsController(mockMediator.Object);

    // Act
    var result = await controller.Create(command);

    // Assert
    var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
    Assert.Equal(expectedProductId, createdResult.RouteValues["id"]);
}
```

## Integration Testing with Open Generic Handlers

Open generic handlers can be verified in integration tests the same way as any other pipeline handler. The key is to register the open generic alongside concrete handlers and assert that it participates in the pipeline.

```csharp
public class OpenGenericHandlerIntegrationTests
{
    [Fact]
    public async Task OpenGenericPreHandler_ShouldExecuteForAllCommands()
    {
        // Arrange
        var executionLog = new List<string>();

        var services = new ServiceCollection();
        services.AddSingleton(executionLog);

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(module =>
            {
                // Register the open generic
                module.Register(typeof(LoggingPreHandler<>));
                module.Register<CreateProductCommandHandler>();
                module.Register<UpdateStockLevelCommandHandler>();
            });
        });

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICommandMediator>();

        // Act
        await mediator.SendAsync(new CreateProductCommand { Name = "Test" });
        await mediator.SendAsync(new UpdateStockLevelCommand { ProductId = Guid.NewGuid(), NewQuantity = 5 });

        // Assert: open generic ran for both commands
        executionLog.Should().Contain("PreHandle:CreateProductCommand");
        executionLog.Should().Contain("PreHandle:UpdateStockLevelCommand");
    }
}

// The open generic handler under test
public sealed class LoggingPreHandler<T> : ICommandPreHandler<T> where T : ICommand
{
    private readonly List<string> _log;
    public LoggingPreHandler(List<string> log) => _log = log;

    public Task PreHandleAsync(T message, CancellationToken cancellationToken = default)
    {
        _log.Add($"PreHandle:{typeof(T).Name}");
        return Task.CompletedTask;
    }
}
```

## Integration Testing with `WebApplicationFactory`

Each `AddLiteBus` call builds a fresh `IMessageRegistry` scoped to that module configuration, so `WebApplicationFactory` tests do not share handler registrations across factories in the same process. Register handlers inside `ConfigureTestServices` (or in the application under test) for each factory instance.

```csharp
public class AppWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    private SqliteConnection _connection = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContextPool<AppDbContext>(options => options.UseSqlite(_connection));

            services.AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(module => module.Register<SignUpUserCommandHandler>());
            });

            EnsureDatabaseCreated(services);
        });
    }

    public new void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }
}
```

If you build a `MessageRegistry` manually in a test (not through `AddLiteBus`), create a new instance for each test case so handler and open-generic state do not leak across tests.

## Testing Durable Messaging

Inbox and outbox tests follow the same pattern: register the core module, InMemory or PostgreSQL storage, a dispatcher, then drive acceptance or enqueue followed by `ProcessPendingAsync` on the processor.

### Inbox Processor Test

```csharp
services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(c => c.Register<ProcessPaymentCommandHandler>());
    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UseInMemoryStorage();
        inbox.UseInProcessDispatch();
    });
});

var inbox = provider.GetRequiredService<IInbox>();
await inbox.AcceptAsync(new ProcessPaymentCommand(orderId));

var processor = provider.GetRequiredService<IInboxProcessor>();
await processor.ProcessPendingAsync(CancellationToken.None);
```

### Outbox Processor Test

```csharp
services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddEvents(e => e.Register<OrderSubmittedEventHandler>());
    builder.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.order-submitted", 1);
        outbox.UseInMemoryStorage();
        outbox.UseInProcessDispatch();
    });
});
```

When using `UseInProcessDispatch`, the real event pipeline runs. Mock `IEventMediator` only when testing code outside the dispatch package.

### Registering All Store Roles Manually

`LiteBus.Testing` exposes `TestInboxStore`, `TestOutboxStore`, `TestMessageTransport`, and mediator test doubles (`TestCommandMediator`, `TestQueryMediator`, `TestEventMediator`). Helpers bind every inbox or outbox store role to one shared instance:

```csharp
var store = new InMemoryInboxStore();
services.AddInboxStoreRoles(store);
```

Use the same pattern with `AddOutboxStoreRoles` for outbox tests. Shared contract suites in `LiteBus.Storage.Testing` inherit `InboxStoreContractTests` and `OutboxStoreContractTests` to validate leasing, retry visibility, dead-letter, and lease reclaim across store implementations.

### Lease and Retry Simulation

InMemory stores honor processor lease duration through `TimeProvider`. Register a manual time provider to advance clocks without `Task.Delay`:

```csharp
services.AddSingleton<TimeProvider>(new ManualTimeProvider(DateTimeOffset.UtcNow));
```

See [Testing](README.md) for InMemory processor recipes and quick CI filters. For the complete integration test map (projects, Docker setup, scenario matrices), see [Integration Tests](integration-tests.md).

## PostgreSQL Integration Tests (Docker Required)

`LiteBus.Storage.IntegrationTests` uses Testcontainers to start `postgres:16-alpine`. Docker must be running before you execute the full solution test suite.

```bash
dotnet test LiteBus.slnx
```

If Docker is not available, those four tests are reported as **skipped** with this message:

> PostgreSQL integration tests require Docker. Start Docker Desktop (or the Docker daemon) and run the tests again.

Start Docker Desktop on Windows or macOS, or confirm the Docker daemon is running on Linux, then rerun the tests. CI runs `docker info` before testing so agents fail fast when Docker is misconfigured.

To run unit tests only:

```bash
dotnet test LiteBus.slnx --filter "FullyQualifiedName!~PostgreSql"
```

## Best Practices

1.  **Unit Test Handlers in Isolation**: The majority of your business logic lives in handlers. Focus your testing efforts here with simple unit tests.
2.  **Use Mocks for Dependencies**: When unit testing a handler, mock its dependencies (repositories, services, etc.) so you test only the handler's logic.
3.  **Integration Test Key Pipelines**: Write a few integration tests for your most critical command/query pipelines to confirm that pre-handlers, main handlers, and post-handlers work together correctly.
4.  **Use an In-Memory Database**: For integration tests involving repositories, use an in-memory database provider (like EF Core's InMemory provider or a real database in a test container) so tests stay fast and isolated.
5.  **Durable messaging tests**: Use InMemory storage and `ProcessPendingAsync` for fast inbox/outbox processor tests; inherit shared store contract tests when validating custom stores.
6.  **`WebApplicationFactory` Tests**: Register LiteBus modules inside each factory's `ConfigureTestServices` so handler discovery uses that factory's own `IMessageRegistry` instance.

## Next
