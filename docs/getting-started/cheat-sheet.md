# LiteBus Cheat Sheet

One-page reference for LiteBus v6. Install only the packages you use; register storage, dispatch, and processors inside nested inbox/outbox builders.

## Mediators

Inject `ICommandMediator`, `IQueryMediator`, or `IEventMediator`. Handlers and pipeline stages (pre, post, error) are discovered by `RegisterFromAssembly` and resolved per mediation call.

```csharp
await commandMediator.SendAsync(new CreateOrderCommand(orderId));
var order = await queryMediator.QueryAsync(new GetOrderQuery(orderId));
await eventMediator.PublishAsync(new OrderPlaced(orderId));
```

## Module Registration

Prefer the `ILiteBusBuilder` overload when contracts are shared across modules:

```csharp
services.AddLiteBus(builder =>
{
    var assembly = typeof(Program).Assembly;

    builder.Contracts.Register<OrderPlaced>("orders.events.placed", 1);

    builder.Modules.AddMessageModule(_ => { });
    builder.Modules.AddCommandModule(c => c.RegisterFromAssembly(assembly));
    builder.Modules.AddQueryModule(q => q.RegisterFromAssembly(assembly));
    builder.Modules.AddEventModule(e => e.RegisterFromAssembly(assembly));

    builder.Modules.AddInboxModule(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UseInMemoryStorage();
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(2));
    });

    builder.Modules.AddOutboxModule(outbox =>
    {
        outbox.Contracts.Register<OrderPlaced>("orders.events.placed", 1);
        outbox.UseInMemoryStorage();
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(2));
    });
});
```

## Durable Messaging Entry Points

| Intent | API | Package role |
| --- | --- | --- |
| Accept command for later execution | `IInbox.AcceptAsync` | `LiteBus.Inbox` |
| Enqueue event for later publication | `IOutbox.EnqueueAsync` | `LiteBus.Outbox` |
| Schedule with future visibility | `InboxAcceptMetadata.Visibility` / `OutboxEnqueueMetadata.Visibility` | `MessageVisibility.At` or `MessageVisibility.After` on the writer item |
| Execute command now | `ICommandMediator.SendAsync` | `LiteBus.Commands` |
| Publish event now | `IEventMediator.PublishAsync` | `LiteBus.Events` |

Only `ICommand` (no result) may be stored in the inbox. Analyzer LB1004 flags `ICommand<TResult>` at compile time.

## Inbox Composition (`Use*` Extensions)

| Extension | NuGet package |
| --- | --- |
| `UseInMemoryStorage()` | `LiteBus.Inbox.Storage.InMemory` |
| `UsePostgreSqlStorage(...)` | `LiteBus.Inbox.Storage.PostgreSql` |
| `UseEntityFrameworkCoreStorage(...)` | `LiteBus.Inbox.Storage.EntityFrameworkCore` |
| `UseInProcessDispatch()` | `LiteBus.Inbox.Dispatch.InProcess` |
| `UseAmqpDispatch(..., connectionOptions)` | `LiteBus.Inbox.Dispatch.Amqp` |
| `UseAzureServiceBusDispatch(..., transportOptions)` | `LiteBus.Inbox.Dispatch.AzureServiceBus` |
| `UseAwsSqsDispatch(..., transportOptions)` | `LiteBus.Inbox.Dispatch.AwsSqs` |
| `UseKafkaDispatch(..., transportOptions)` | `LiteBus.Inbox.Dispatch.Kafka` |
| `UseInMemoryDispatch(...)` | `LiteBus.Inbox.Dispatch.InMemory` |
| `UseAmqpIngress(...)` | `LiteBus.Inbox.Ingress.Amqp` |
| `UseAzureServiceBusIngress(...)` | `LiteBus.Inbox.Ingress.AzureServiceBus` |
| `UseAwsSqsIngress(...)` | `LiteBus.Inbox.Ingress.AwsSqs` |
| `UseKafkaIngress(...)` | `LiteBus.Inbox.Ingress.Kafka` |
| `UseInMemoryIngress(...)` | `LiteBus.Inbox.Ingress.InMemory` |
| `EnableInboxProcessor(...)` | `LiteBus.Inbox` (registers `InboxProcessorBackgroundService` via manifest) |
| `EnableSaga(...)` | `LiteBus.Saga.InboxIntegration` |

## Outbox Composition (`Use*` Extensions)

| Extension | NuGet package |
| --- | --- |
| `UseInMemoryStorage()` | `LiteBus.Outbox.Storage.InMemory` |
| `UsePostgreSqlStorage(...)` | `LiteBus.Outbox.Storage.PostgreSql` |
| `UseEntityFrameworkCoreStorage(...)` | `LiteBus.Outbox.Storage.EntityFrameworkCore` |
| `UseInProcessDispatch()` | `LiteBus.Outbox.Dispatch.InProcess` |
| `UseAmqpDispatch(..., connectionOptions)` | `LiteBus.Outbox.Dispatch.Amqp` |
| `UseAzureServiceBusDispatch(..., transportOptions)` | `LiteBus.Outbox.Dispatch.AzureServiceBus` |
| `UseAwsSqsDispatch(..., transportOptions)` | `LiteBus.Outbox.Dispatch.AwsSqs` |
| `UseKafkaDispatch(..., transportOptions)` | `LiteBus.Outbox.Dispatch.Kafka` |
| `UseInMemoryDispatch(...)` | `LiteBus.Outbox.Dispatch.InMemory` |
| `EnableOutboxProcessor(...)` | `LiteBus.Outbox` |

## Accept and Enqueue Examples

```csharp
var receipt = await inbox.AcceptAsync(
    InboxAcceptItem<ProcessPaymentCommand>.From(
        new ProcessPaymentCommand(paymentId, amount),
        InboxAcceptMetadata.Immediate with
        {
            Idempotency = new Idempotency.Keyed($"payment:{paymentId}"),
        }),
    cancellationToken);

var outboxReceipt = await outbox.EnqueueAsync(
    OutboxEnqueueItem<OrderPlaced>.WithTopic(new OrderPlaced(orderId), "orders.events"),
    cancellationToken);
```

## PostgreSQL Storage (Nested)

```csharp
builder.Modules.AddInboxModule(inbox =>
{
    inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
    inbox.UsePostgreSqlStorage(pg =>
    {
        pg.UseDataSource(dataSource);
        pg.EnsureSchemaCreationOnStartup(); // dev only; production uses migration-owned DDL
    });
    inbox.UseInProcessDispatch();
    inbox.EnableInboxProcessor();
});
```

Schema is version **1** only. There is no upgrade path from older LiteBus schemas; drop and recreate tables when adopting v6.

## Health and Operations

```csharp
builder.Services.AddLiteBusManagement(options =>
{
    options.FailHealthWhenNoProbes = false; // samples; production should use true (default)
    options.AuthorizationPolicy = "LiteBusOperator"; // production
    // options.AllowAnonymousManagement = true; // Development only
});
builder.Services.AddHealthChecks().AddLiteBus();
app.UseAuthentication();
app.UseAuthorization();
app.AddLiteBusManagementEndpoints();
```

Register `IDiagnosticCheck` probes on the inbox or outbox builder with `AddDiagnosticCheck<T>(name)`.

## OpenTelemetry

The aggregate `LiteBus.Extensions.OpenTelemetry` package is removed. Register each axis you export:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddLiteBusInboxMetrics()
        .AddLiteBusOutboxMetrics()
        .AddLiteBusTransportMetrics());
```

Instrument names on `LiteBusInboxTelemetry`, `LiteBusOutboxTelemetry`, and `LiteBusTransportTelemetry` are a public contract; renames are breaking changes.

## Processor Options Worth Knowing

| Option | Default | Notes |
| --- | --- | --- |
| `HonorShutdownTokenOnPersist` | `false` | When `false`, terminal `PersistAsync` uses `CancellationToken.None` (safer against duplicate dispatch; shutdown may block). When `true`, passes the shutdown token (faster drain; duplicate-dispatch risk). |
| `DispatcherConcurrency` | `1` | Raise only when handlers are safe in parallel. |
| `LeaseHeartbeatInterval` | 15s | Must be `<= LeaseDuration / 2`. |

## Naming Schemes

| You see | Meaning |
| --- | --- |
| Folder / project `LiteBus.Inbox.Storage.PostgreSql` | Source layout |
| Namespace `LiteBus.Inbox.Storage.PostgreSql` | C# API |
| NuGet `LiteBus.Inbox.Storage.PostgreSql` | Package ID |

## Next

- [Getting Started](README.md) for a full walkthrough
- [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) for durable messaging depth
- [Hosted services](../architecture/hosted-services.md) for manifest, probes, and management endpoints
- [Dependency Graph](../architecture/dependency-graph.md) for package boundaries
