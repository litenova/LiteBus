# ASP.NET Core with EF Core Outbox Registration

An ASP.NET Core host that runs commands through an Entity Framework Core unit of work and publishes events through an EF Core outbox needs one registration that ties messaging, commands, events, storage, dispatch, the processor, cleanup, health checks, and management endpoints together. This page is that registration as a single file. Every statement is marked required or optional, with the failure mode of omitting it. The shorter guides link here instead of repeating partial registration.

> **This page is for** developers who wire LiteBus into an ASP.NET Core host backed by EF Core.
>
> **After reading it** you can register the full stack in `Program.cs` and explain what breaks when any statement is missing. For the concepts behind each call, follow the links in the reference table.

## Packages to Install

```bash
dotnet add package LiteBus.Commands.Extensions.Microsoft.DependencyInjection
dotnet add package LiteBus.Events.Extensions.Microsoft.DependencyInjection
dotnet add package LiteBus.Outbox.Storage.EntityFrameworkCore
dotnet add package LiteBus.Outbox.Dispatch.InProcess
dotnet add package LiteBus.Extensions.Diagnostics.HealthChecks
dotnet add package LiteBus.Extensions.AspNetCore
```

The semantic `*.Extensions.Microsoft.DependencyInjection` packages pull in the messaging core and the `AddLiteBus` runtime adapter transitively. The outbox storage and dispatch packages pull in the `LiteBus.Outbox` core transitively. Install only the modules your host runs.

| Extension | NuGet package |
| --- | --- |
| `UseEntityFrameworkCoreStorage(...)` | `LiteBus.Outbox.Storage.EntityFrameworkCore` |
| `UseInProcessDispatch()` | `LiteBus.Outbox.Dispatch.InProcess` |
| `AddLiteBus()` on `IHealthChecksBuilder` | `LiteBus.Extensions.Diagnostics.HealthChecks` |
| `AddLiteBusManagement()` and `AddLiteBusManagementEndpoints()` | `LiteBus.Extensions.AspNetCore` |

## The Complete Registration

<!-- snippet-source: samples/LiteBus.Samples.Documentation/Program.cs#efcore-outbox-program -->
```csharp
var builder = WebApplication.CreateBuilder(args);

// Required: one factory registration. EfCoreOutboxStore creates and disposes one context per
// operation through IDbContextFactory<OrdersDbContext>, and the same call registers
// OrdersDbContext as a scoped service, which ITransactionalOutbox<OrdersDbContext> resolves.
// Configure the database provider (such as options.UseNpgsql(connectionString)) in this
// lambda alongside the interceptor.
builder.Services.AddDbContextFactory<OrdersDbContext>((serviceProvider, options) =>
    options.AddLiteBusOutboxInterceptor(
        serviceProvider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>()));

// Required: messaging, commands with a unit of work, events, and the outbox.
builder.Services.AddLiteBus(liteBus =>
{
    // Required: message contracts, serialization, and the mediation core.
    // The outbox module declares a dependency on messaging and does not register it.
    liteBus.AddMessaging(_ =>
    {
    });

    liteBus.AddCommands(commands =>
    {
        // Required: discovers command handlers in the application assembly.
        commands.RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly);

        // Required: commits the staged unit of work after every successful mediation.
        // EnableImmediateDispatch fails composition without a registered IUnitOfWork.
        commands.UseUnitOfWork<OrderUnitOfWork>();
    });

    liteBus.AddEvents(events =>
    {
        // Required: discovers the event handlers in-process dispatch publishes to.
        events.RegisterFromAssembly(typeof(OrderSubmitted).Assembly);
    });

    liteBus.AddOutbox(outbox =>
    {
        // Required: one stable contract name and version per stored event type.
        outbox.Contracts.Register<OrderSubmitted>("orders.events.submitted", 1);

        outbox.UseEntityFrameworkCoreStorage(ef =>
        {
            // Required: binds the store and ITransactionalOutbox<TContext> to OrdersDbContext.
            ef.UseDbContext<OrdersDbContext>();

            // Required: registers LiteBusOutboxSaveChangesInterceptor in DI so the
            // AddDbContextFactory call above can attach it.
            ef.EnableSaveChangesInterceptor();

            // Optional but recommended: fails module build when EnableSaveChangesInterceptor
            // was not called, instead of failing at the first SaveChanges.
            ef.EnforceTransactionalSetup();
        });

        // Required: exactly one dispatcher. The processor has nothing to dispatch through
        // without it, and module build fails when the processor is enabled without one.
        outbox.UseInProcessDispatch();

        // Optional: one envelope per subscribed handler, each with its own retry and replay.
        // Required when a handler class carries [DispatchImmediately]: without per-handler
        // delivery, only an event type carrying the attribute is immediate.
        outbox.EnablePerHandlerDelivery();

        // Optional: dispatches staged immediate envelopes right after the commit, with the
        // processor as the fallback. Requires the unit of work, the dispatcher, and a store
        // implementing IOutboxTargetedLeaseStore; FallbackDelay must exceed Timeout.
        outbox.EnableImmediateDispatch(dispatch =>
        {
            dispatch.Timeout = TimeSpan.FromSeconds(10);
            dispatch.FallbackDelay = TimeSpan.FromSeconds(30);
        });

        // Required for background publication: polls due envelopes and dispatches them.
        // Without it, only immediate dispatch (when enabled) publishes anything.
        outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));

        // Optional: deletes terminal rows on a schedule. Without it, published rows
        // accumulate. Retention must be set, or the loop runs and deletes nothing.
        outbox.EnableCleanup(cleanup =>
        {
            cleanup.Interval = TimeSpan.FromHours(1);
            cleanup.Retention = TimeSpan.FromDays(30);
        });
    });
});

// Optional: exposes manifest diagnostic probes as an ASP.NET Core health check.
// Without it, probes still run through GET /litebus/health when mapped below.
builder.Services.AddHealthChecks().AddLiteBus(options =>
{
    options.FailHealthWhenNoProbes = true;
    options.DiagnosticChecks = new DiagnosticCheckRunOptions
    {
        Timeout = TimeSpan.FromSeconds(3),
        MaxParallelism = 4,
        RunTimeout = TimeSpan.FromSeconds(10)
    };
});

// Optional: registers management endpoint options. Without it, the routes below
// cannot be mapped and operators have no HTTP access to rows or processor state.
builder.Services.AddLiteBusManagement(options =>
{
    options.AuthorizationPolicy = "LiteBusOperator";
});

var app = builder.Build();

// Optional host wiring: exposes the health check registered above.
app.MapHealthChecks("/health");

// Optional: maps inbox and outbox operator routes plus GET /litebus/health.
// Requires authentication unless AllowAnonymousManagement is set for local demos.
app.AddLiteBusManagementEndpoints();

app.Run();
```

The two context lambdas show the interceptor call only; the compiled snippet behind this block configures no database provider. Add the provider call for the database in use (such as `options.UseNpgsql(connectionString)`) in the same lambdas, on both the scoped context and the factory.

## Registration Line Reference

| Statement | Required or optional | Failure mode of omitting it |
| --- | --- | --- |
| `AddDbContextFactory<OrdersDbContext>` with the interceptor | Required | The singleton store has no `IDbContextFactory<OrdersDbContext>` to create operation contexts, and no scoped `OrdersDbContext` is registered, so background reads, leases and processor writes fail and the startup check stops the host naming the missing registration. The same call registers the scoped context that `ITransactionalOutbox<OrdersDbContext>` resolves. If the application also calls `AddDbContext<OrdersDbContext>`, pass `optionsLifetime: ServiceLifetime.Singleton`; otherwise the singleton factory reads scoped option configuration and scope validation fails in Development. |
| Interceptor on the factory registration | Required | Envelopes queued on a context whose options lack the interceptor are never flushed into `OutboxMessages` during `SavingChanges`: the domain change commits and the event is silently lost. |
| Provider call (such as `UseNpgsql`) inside the factory lambda | Required | Without a provider the context has no database to connect to, so every read and write fails when the context is first used. |
| `AddMessaging` | Required | Composition fails: the outbox module requires the messaging module and no semantic module registers it. See [Getting Started](README.md). |
| `RegisterFromAssembly` on commands and events | Required | Handlers are unknown: commands have no handler at mediation time, and in-process dispatch finds no subscriber for stored events. |
| `UseUnitOfWork<OrderUnitOfWork>` | Required | Nothing commits the staged work after a successful mediation, and `EnableImmediateDispatch` fails composition without a registered `IUnitOfWork`. See [Domain Events and Unit of Work](../reliable-messaging/domain-events-and-unit-of-work.md). |
| `Contracts.Register<OrderSubmitted>` | Required per stored type | Enqueue fails contract resolution for the unregistered event type. |
| `UseDbContext<OrdersDbContext>` | Required | Module build throws `DurableStorageConfigurationException` naming `UseDbContext<TContext>()`, and `ITransactionalOutbox<OrdersDbContext>` is never registered. |
| `EnableSaveChangesInterceptor` | Required | The interceptor is not in DI, so the `AddDbContext` options lambdas throw when a context is first built. |
| `EnforceTransactionalSetup` | Optional, recommended | A missing `EnableSaveChangesInterceptor` call surfaces at the first `SaveChanges` instead of failing module build at startup. |
| `UseInProcessDispatch` | Required (exactly one dispatcher) | Module build fails when the processor is enabled without a dispatcher. Registering a second dispatcher throws. |
| `EnablePerHandlerDelivery` | Optional; required for handler-level `[DispatchImmediately]` | One envelope per event: no per-handler retry or replay, and a handler-level attribute has no immediate effect. |
| `EnableImmediateDispatch` | Optional | Immediate handlers wait for the processor poll instead of running right after the commit. See [Immediate Dispatch](../reliable-messaging/immediate-dispatch.md). |
| `EnableOutboxProcessor` | Required for background publication | Stored envelopes are never leased or dispatched, except through immediate dispatch when enabled. |
| `EnableCleanup` with `Retention` | Optional | Terminal rows accumulate. Enabling cleanup without `Retention` runs a loop that deletes nothing. |
| `AddHealthChecks().AddLiteBus(...)` | Optional | No readiness signal through ASP.NET Core health checks. Probes still run through the management health route when mapped. See [Diagnostics and Health](../operations/diagnostics-and-health.md). |
| `AddLiteBusManagement` and `AddLiteBusManagementEndpoints` | Optional | No operator routes for message queries, processor control, retention, or diagnostics. See [Management Endpoints](../operations/management-endpoints.md). |

## Supporting Types

The registration above references two application types. The context implements `IOutboxDbContext` and maps the outbox table in `OnModelCreating`:

<!-- snippet-source: samples/LiteBus.Samples.Documentation/Orders/OrdersDbContext.cs#efcore-outbox-db-context -->
```csharp
namespace LiteBus.Samples.Documentation.Orders;

/// <summary>
///     Stores order state with the outbox rows committed in the same transaction.
/// </summary>
public sealed class OrdersDbContext : DbContext, IOutboxDbContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OrdersDbContext" /> class.
    /// </summary>
    /// <param name="options">The configured context options.</param>
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.GetModelBuilderConfiguration(
            new EntityFrameworkCoreOutboxStoreOptions(),
            EfCoreStorageProvider.PostgreSql);
    }
}
```

The unit of work commits the scoped context. EF Core needs no explicit begin, because `SaveChangesAsync` is the commit:

<!-- snippet-source: samples/LiteBus.Samples.Documentation/Orders/OrderUnitOfWork.cs#efcore-outbox-unit-of-work -->
```csharp
namespace LiteBus.Samples.Documentation.Orders;

/// <summary>
///     Commits the scoped order context after a successful mediation.
/// </summary>
public sealed class OrderUnitOfWork : IUnitOfWork
{
    /// <summary>
    ///     The scoped context that stages order changes.
    /// </summary>
    private readonly OrdersDbContext _dbContext;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OrderUnitOfWork" /> class.
    /// </summary>
    /// <param name="dbContext">The scoped context that stages order changes.</param>
    public OrderUnitOfWork(OrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

The command handler stages the event through `ITransactionalOutbox<OrdersDbContext>` before the commit, so the outbox row joins the domain transaction:

<!-- snippet-source: samples/LiteBus.Samples.Documentation/OutboxEnqueueSnippet.cs#efcore-outbox-enqueue -->
```csharp
await transactionalOutbox.EnqueueAsync(
    new OrderSubmitted { OrderId = order.Id },
    cancellationToken).ConfigureAwait(false);
```

A handler whose effect must be visible before the caller continues carries `[DispatchImmediately]` and needs per-handler delivery plus `EnableImmediateDispatch` from the registration above:

<!-- snippet-source: samples/LiteBus.Samples.Documentation/Orders/OrderProjectionHandler.cs#efcore-outbox-immediate-handler -->
```csharp
namespace LiteBus.Samples.Documentation.Orders;

/// <summary>
///     Updates the order projection as soon as the order event is published.
/// </summary>
[DispatchImmediately]
public sealed class OrderProjectionHandler : IEventHandler<OrderSubmitted>
{
    /// <summary>
    ///     The projection store updated by the handler.
    /// </summary>
    private readonly IOrderProjectionStore _projectionStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OrderProjectionHandler" /> class.
    /// </summary>
    /// <param name="projectionStore">The projection store updated by the handler.</param>
    public OrderProjectionHandler(IOrderProjectionStore projectionStore)
    {
        _projectionStore = projectionStore;
    }

    /// <inheritdoc />
    public Task HandleAsync(OrderSubmitted message, CancellationToken cancellationToken = default)
    {
        return _projectionStore.UpsertAsync(message.OrderId, cancellationToken);
    }
}
```

## Namespaces

| Type | Namespace |
| --- | --- |
| `ICommandMediator`, `ICommandHandler<TCommand>` | `LiteBus.Commands.Abstractions` |
| `IEventHandler<TEvent>` | `LiteBus.Events.Abstractions` |
| `IUnitOfWork`, `DispatchImmediatelyAttribute` | `LiteBus.Messaging.Abstractions` |
| `IOutbox`, `ITransactionalOutbox<TContext>`, `OutboxEnqueueItem<TEvent>` | `LiteBus.Outbox.Abstractions` |
| `IOutboxDbContext`, `OutboxMessageEntity`, `LiteBusOutboxSaveChangesInterceptor` | `LiteBus.Outbox.Storage.EntityFrameworkCore` |
| `LiteBusHealthCheckOptions` and the `AddLiteBus` health extension | `LiteBus.Extensions.Diagnostics.HealthChecks` |
| `DiagnosticCheckRunOptions` | `LiteBus.Runtime.Abstractions.Diagnostics` |
| `AddLiteBusManagement`, `AddLiteBusManagementEndpoints` | `LiteBus.Extensions.AspNetCore` |

`LiteBusHealthCheckOptions.FailHealthWhenNoProbes` is read by `LiteBusHealthCheck` when the check runs, not by the `AddLiteBus` extension method, which only validates `DiagnosticChecks`. The documented shape above matches the source members exactly.

## Next

- [Transactional Messaging Writes](../reliable-messaging/transactional-writes.md) for writer choice and interceptor behavior
- [Outbox Entity Framework Core Storage](../integrations/outbox-ef-core-storage.md) for schema ownership, columns, and provider mapping
- [Immediate Dispatch](../reliable-messaging/immediate-dispatch.md) for await versus background mode and fallback monitoring
- [Diagnostics and Health](../operations/diagnostics-and-health.md) for probes and metric export
- [Management Endpoints](../operations/management-endpoints.md) for the route inventory and authorization
