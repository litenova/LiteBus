# Testing LiteBus

Handlers take dependencies through constructors, so a test calls the handler method directly with doubles, and integration tests run the wired pipeline through a real service provider. This page covers both levels: unit tests for handler logic, host-based tests for the mediation pipeline and durable processors, and the test-double packages LiteBus ships. It assumes you have read [Getting Started](../getting-started/README.md).

## Unit Testing Handlers

Handlers take dependencies through constructors, so a test calls the handler method directly with doubles. LiteBus infrastructure is not needed to test handler logic.

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
    private readonly Mock<IEventMediator> _mockEventMediator = new();

    public CommandIntegrationTests()
    {
        var services = new ServiceCollection();

        // Register mock dependencies
        services.AddSingleton<IProductRepository, InMemoryProductRepository>();
        services.AddSingleton(_mockEventMediator.Object); // Mock for the post-handler

        // Configure LiteBus with all relevant handlers
        services.AddLiteBus(builder =>
        {
            builder.AddMessaging(_ => { });
            builder.AddCommands(module =>
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
        _mockEventMediator.Verify(p => p.PublishAsync(It.IsAny<ProductCreatedEvent>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

Keep the `Mock<IEventMediator>` in a field for `Verify` and register its `.Object`. The container holds an `IEventMediator` instance, so resolving `Mock<IEventMediator>` from the provider throws. Alternatively, use the recording `TestEventMediator` from `LiteBus.Testing.Mediation` and assert against its `Events` collection.

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

### Recording Mediator Doubles

`LiteBus.Testing.Mediation` ships recording doubles that implement the mediator interfaces without running the pipeline:

| Double | Records | Returns |
| --- | --- | --- |
| `TestCommandMediator` | `Commands` for `SendAsync` and `TrySendAsync`; `Evaluated` for `EvaluateAsync` | `default(TCommandResult)` for result sends; `MediationResult.Succeeded()` for `TrySendAsync`; `MediationDecision.Allowed` for `EvaluateAsync` |
| `TestQueryMediator` | `Queries` for `QueryAsync`, `StreamAsync`, and `TryQueryAsync`; `Evaluated` for `EvaluateAsync` | `NextResult` cast to the query result type, consumed once per call; a stub of the wrong type throws `InvalidCastException` |
| `TestEventMediator` | `Events` for both `PublishAsync` overloads | Nothing to return |

Each double has a `Clear` method that empties its recordings. All doubles are thread-safe and their recording properties return snapshots, so concurrent sends are all recorded and a held reference never observes later messages. `NextResult` is single-use: each query, stream, or tried query consumes it, so stub it before every call. Use them when the test asserts that a message was sent, not what the pipeline did with it:

```csharp
var commands = new TestCommandMediator();
var service = new OrderService(commands);

await service.PlaceOrderAsync(orderId, CancellationToken.None);

Assert.Single(commands.Commands, c => c is PlaceOrderCommand);
```

A recording double runs no guards, so `EvaluateAsync` can only answer that nothing objected. Assert the decision itself against the real pipeline through `MediationHarness` or a host. `LiteBusTestBase`, from the framework-neutral `LiteBus.Testing` package, is an `IAsyncDisposable` base class for tests that participate in shared test infrastructure disposal.

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

        services.AddLiteBus(builder =>
        {
            builder.AddMessaging(_ => { });
            builder.AddCommands(module =>
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

            services.AddLiteBus(builder =>
            {
                builder.AddMessaging(_ => { });
                builder.AddCommands(module => module.Register<SignUpUserCommandHandler>());
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

Testing support is split so a mediator test does not pull in durable storage or a host adapter:

| Package | Support |
| --- | --- |
| `LiteBus.Testing` | `ManualTimeProvider`, `LiteBusTestBase` |
| `LiteBus.Testing.Mediation` | `TestCommandMediator`, `TestQueryMediator`, `TestEventMediator` |
| `LiteBus.Testing.Transport` | `TestMessageTransport` |
| `LiteBus.Testing.DurableMessaging` | `TestInboxStore`, `TestOutboxStore`, `InboxOutboxTestHost`, store-role and processor-pass helpers, test transactional inbox and outbox, `ScriptedOutboxDispatcher`, `StoreSchemaContract` |
| `LiteBus.Testing.Hosting` | Generic Host start, stop, manifest, and background-service helpers |

The durable-messaging package can bind every inbox or outbox store role to one shared instance:

```csharp
var store = new InMemoryInboxStore();
services.AddInboxStoreRoles(store);
```

Use the same pattern with `AddOutboxStoreRoles` for outbox tests. Shared contract suites in `LiteBus.Storage.Testing` inherit `InboxStoreContractTests` and `OutboxStoreContractTests` to validate leasing, retry visibility, dead-letter, and lease reclaim across store implementations.

### Inbox and Outbox Test Host

`InboxOutboxTestHost.Create`, from `LiteBus.Testing.DurableMessaging`, builds an isolated service provider with in-memory inbox and outbox storage, an optional `TimeProvider`, and optional module configuration. The host registers no message contracts and no dispatcher, so register the contract on the resolved `IMessageContractRegistry` and add an `IInboxDispatcher` through `configureServices` before driving accept and processor passes. It proves accept, enqueue, and processor-pass behavior without asserting anything about production storage:

```csharp
await using var host = InboxOutboxTestHost.Create(
    configure: builder => builder.AddCommands(c => c.Register<ProcessPaymentCommandHandler>()),
    configureServices: services => services.AddSingleton<IInboxDispatcher>(
        provider => new CommandInboxDispatcher(
            provider.GetRequiredService<ICommandMediator>(),
            provider.GetRequiredService<IContractReader>(),
            provider.GetRequiredService<IMessageSerializer>())),
    timeProvider: new ManualTimeProvider(DateTimeOffset.UtcNow));

host.Services.GetRequiredService<IMessageContractRegistry>().Register<ProcessPaymentCommand>("payments.process-payment", 1);

var inbox = host.GetInbox();
var receipt = await inbox.AcceptAsync(new ProcessPaymentCommand(orderId));

await ProcessorPassHelpers.RunInboxPassAsync(host.Services, CancellationToken.None);
```

`GetInbox` and `GetOutbox` resolve the writers. `ProcessorPassHelpers.RunInboxPassAsync` and `RunOutboxPassAsync` resolve the processor from the provider and run one pass, returning the leased, succeeded, failed, and dead-lettered counts.

### Store Doubles and Lease Chaos

`TestInboxStore` and `TestOutboxStore` implement the writer, processing, and operations roles on one process-local instance backed by the in-memory store. Each one exposes `Get(messageId)` to read a stored envelope and `Clear` to empty the store between tests. Both doubles accept optional store options and a `TimeProvider` through their constructors, and both carry the transactional writer marker so the test transactional helpers can bind them:

```csharp
var store = new TestInboxStore(timeProvider: timeProvider);
services.AddInboxStoreRoles(store);

// After the pass under test:
var envelope = store.Get(receipt.Id);
Assert.Equal(InboxStatus.Completed, envelope.Status);
```

`ChaosLeaseExpiryFixture` wraps an `InMemoryInboxStore` and returns a lease store from `CreateLeaseStore` that reports renewal failure for one target message, which simulates a lease lost mid-dispatch. It proves the stale-lease path, not production lease timing.

### Testing the Outbox Without a Database

`AddTestTransactionalOutbox` binds one shared `TestOutboxStore` to every outbox role and registers an in-memory `ITransactionalOutbox`, so application tests enqueue through the transactional API and drive the processor without a database. Call it after `AddLiteBus` so the test roles win over module registrations, resolve the transactional outbox from a scope, then run `DrainOutboxAsync` to publish everything pending in one call without polling waits:

```csharp
var store = new TestOutboxStore();
var scripted = new ScriptedOutboxDispatcher();

var provider = new ServiceCollection()
    .AddLiteBus(builder =>
    {
        builder.AddMessaging(_ => { });
        builder.AddOutbox(outbox =>
        {
            outbox.Contracts.Register<OrderPlaced>("orders.placed");
            outbox.Contracts.Register<PaymentTaken>("payments.taken");
            outbox.UseProcessorOptions(new OutboxProcessorOptions
            {
                Retry = new RetryOptions { MaxAttempts = 3, InitialDelay = TimeSpan.Zero, UseJitter = false }
            });
            outbox.UseInMemoryStorage();
            outbox.RegisterDispatcher(new FixedOutboxDispatchModule(scripted));
        });
    })
    .AddTestTransactionalOutbox(store)
    .BuildServiceProvider();

using var scope = provider.CreateScope();
var outbox = scope.ServiceProvider.GetRequiredService<ITransactionalOutbox>();

scripted.FailAlways("orders.placed", new InvalidOperationException("broker unreachable"));

var orderReceipt = await outbox.EnqueueAsync(new OrderPlaced(orderId));
var paymentReceipt = await outbox.EnqueueAsync(new PaymentTaken(orderId));

var drain = await ProcessorPassHelpers.DrainOutboxAsync(provider);

Assert.Equal(OutboxStatus.DeadLettered, store.Get(orderReceipt.Id).Status);
Assert.Equal(3, scripted.GetAttemptCount("orders.placed"));
Assert.Equal(OutboxStatus.Published, store.Get(paymentReceipt.Id).Status);
```

`ScriptedOutboxDispatcher` fails scripted contract names while the rest dispatch normally: `FailNext` fails the next N dispatches of one contract, `FailAlways` fails every dispatch until cleared, `GetAttemptCount` reports per-contract attempts, and `AttemptedEnvelopes` and `DispatchedEnvelopes` record what the processor tried. Without an inner dispatcher, unscripted dispatches succeed and are recorded; pass an inner dispatcher to decorate a real transport and prove retry, backoff, and dead-letter transitions against it. A canceled dispatch throws `OperationCanceledException` without consuming a script or touching inner.

A fixed test dispatcher module is application-owned glue, written once per test project:

```csharp
internal sealed class FixedOutboxDispatchModule : IOutboxDispatcherModule
{
    private readonly IOutboxDispatcher _dispatcher;

    public FixedOutboxDispatchModule(IOutboxDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Build(IModuleConfiguration configuration)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IOutboxDispatcher), _dispatcher));
    }
}
```

`StoreSchemaContract.AssertCompatible` pins the schema identity a diagnostics store reports and throws one `InvalidOperationException` listing every difference instead of a bare version comparison:

```csharp
StoreSchemaContract.AssertCompatible(
    await store.GetSchemaInfoAsync(),
    new StoreSchemaExpectation { Component = "outbox", ExpectedVersion = 1, RecordedVersion = 1 });
```

Three limits apply. The in-memory transactional outbox writes immediately because there is no transaction to enlist in, so it proves enqueue and processor behavior, not rollback. A drain stops when a pass leases nothing, so rows hidden behind a retry backoff need a manual clock advance plus another drain; a drain that still leases after `maxPasses` throws `InvalidOperationException`. The schema assertion checks component, version, and pinned schema and table names only, because `StoreSchemaInfo` exposes no column list. Inbox twins exist: `DrainInboxAsync`, the `IInboxDiagnosticsStore` overload of `AssertCompatibleAsync`, and `AddTestTransactionalInbox`, which binds one shared `TestInboxStore` to every inbox role plus an in-memory `ITransactionalInbox`.

### Transport Publisher Double

`TestMessageTransport`, from `LiteBus.Testing.Transport`, implements `ITransportPublisher` only. It records every publication in `Published`, throws a one-shot `NextPublishException` on the next publish (consumed atomically, so exactly one concurrent publish observes it), and throws `InvalidOperationException` from every publish while `IsDisconnected` is true. `Clear` empties the recordings and resets the failure state:

```csharp
var transport = new TestMessageTransport
{
    NextPublishException = new TimeoutException("broker unreachable")
};

var request = new TransportPublishRequest
{
    Destination = "orders.outbox",
    Body = JsonSerializer.SerializeToUtf8Bytes(new OrderSubmitted(orderId))
};

var exception = await Assert.ThrowsAsync<TimeoutException>(
    () => transport.PublishAsync(request, CancellationToken.None));
Assert.Empty(transport.Published);
```

It has no consume loop and no acknowledgement semantics. Tests for ingress behavior run against the in-memory transport or a broker fixture instead.

### Host Lifecycle Helpers

`LiteBusHostedServiceExtensions`, from `LiteBus.Testing.Hosting`, drives the generic host adapters in tests without building a host. `StartLiteBusHostedServicesAsync` starts every registered `IHostedService`, not only LiteBus ones; `StopLiteBusHostedServicesAsync` stops them in reverse registration order. `GetInboxProcessorHostedService` resolves the adapter that runs the inbox processor loop, and `AssertBackgroundServices` compares the manifest background services against expected implementation types and throws `InvalidOperationException` with an expected-versus-actual diff on mismatch:

```csharp
await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, CancellationToken.None);

try
{
    var manifest = LiteBusHostedServiceExtensions.AssertBackgroundServices(
        provider,
        typeof(InboxProcessorBackgroundService));
    Assert.Contains(typeof(InboxProcessorBackgroundService), manifest.BackgroundServices);
}
finally
{
    await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
}
```

### Lease and Retry Simulation

InMemory stores honor processor lease duration through `TimeProvider`. Thread one manual clock through the messaging and storage builders and advance it without `Task.Delay`:

```csharp
var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);

services.AddLiteBus(builder =>
{
    builder.AddMessaging(messaging => messaging.UseTimeProvider(timeProvider));
    builder.AddInbox(inbox => inbox.UseInMemoryStorage(storage => storage.UseTimeProvider(timeProvider)));
    builder.AddOutbox(outbox => outbox.UseInMemoryStorage(storage => storage.UseTimeProvider(timeProvider)));
});

// After leasing, expire the lease without waiting:
timeProvider.Advance(TimeSpan.FromMinutes(2));
```

Registering the clock directly with `services.AddSingleton<TimeProvider>(timeProvider)` does not work: module composition registers its own `TimeProvider` binding, so a directly registered clock never reaches writers or lease checks. `InboxOutboxTestHost.Create` applies this builder pattern for its `timeProvider` argument.

See [Testing](README.md) for InMemory processor recipes and quick CI filters. For the complete integration test map (projects, Docker setup, scenario matrices), see [Integration Tests](integration-tests.md).

## PostgreSQL Integration Tests (Docker Required)

`LiteBus.Storage.IntegrationTests` uses Testcontainers to start `postgres:16-alpine`. Docker must be running before you execute the full solution test suite.

```bash
dotnet test LiteBus.slnx
```

If Docker is not available, the affected tests are reported as **skipped** with this message:

> PostgreSQL integration tests require Docker. Start Docker Desktop (or the Docker daemon), clear a stale DOCKER_HOST override (tcp://...), and run the tests again.

Start Docker Desktop on Windows or macOS, or confirm the Docker daemon is running on Linux, then rerun the tests. CI runs `docker info` before testing so the job fails before starting integration tests when Docker is misconfigured.

To run unit tests only:

```bash
dotnet test LiteBus.slnx --filter "FullyQualifiedName!~PostgreSql"
```

## Test Levels

Each level needs a different LiteBus package:

1.  **Handler logic**: call the handler method directly with test doubles for its constructor dependencies. No LiteBus package is needed.
2.  **One message's pipeline**: run `MediationHarness` from `LiteBus.Testing.Mediation` over supplied handler instances to assert stages, outcomes, and decisions.
3.  **Wired mediator**: build a real provider with `AddLiteBus` and send through `ICommandMediator`, `IQueryMediator`, or `IEventMediator` to prove registration, lifetimes, and stage composition.
4.  **Durable processors**: register InMemory storage with `UseInProcessDispatch` and drive `AcceptAsync` or `EnqueueAsync` followed by `ProcessPendingAsync`. Inherit the shared store contract suites in `LiteBus.Storage.Testing` when validating a custom store.
5.  **Repositories behind handlers**: use the EF Core InMemory provider or a Testcontainers database so integration tests stay isolated.
6.  **`WebApplicationFactory`**: register LiteBus modules inside each factory's `ConfigureTestServices` so handler discovery uses that factory's own `IMessageRegistry` instance.

## Next

- [Testing](README.md): mediation harness, InMemory recipes, and quick CI filters
- [Integration Tests](integration-tests.md): executor inventory, fixtures, and scenario matrices
