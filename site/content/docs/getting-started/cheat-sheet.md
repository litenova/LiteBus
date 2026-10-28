# LiteBus Cheat Sheet

LiteBus registration and mediation follow a small set of patterns. You install only the packages you use, and you register storage, dispatch, and processors inside nested inbox and outbox builders. This page keeps every registration and mediation call you use daily on one page.

> **This page is for** developers who finished [Getting Started](README.md) and want each API without the walkthrough.
>
> **After reading it** you can compose transports, stores, dispatchers, and mediators from memory. [Getting Started](README.md) holds the full walkthrough for each row.

## Mediators

Inject `ICommandMediator`, `IQueryMediator`, or `IEventMediator`. `RegisterFromAssembly` discovers handlers and pipeline stages (guards, validators, shortcuts, pre-handlers, post-handlers, error handlers, and completion handlers), and each mediation call resolves them.

```csharp
await commandMediator.SendAsync(new CreateOrderCommand(orderId));
var order = await queryMediator.QueryAsync(new GetOrderQuery(orderId));
await eventMediator.PublishAsync(new OrderPlaced(orderId));
```

Mediation alone persists nothing. When the work must survive a restart, accept through `IInbox.AcceptAsync` or enqueue through `IOutbox.EnqueueAsync` instead. See [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md).

## Module Registration

Prefer the `ILiteBusBuilder` overload when composing the features your installed packages provide:

```csharp
services.AddLiteBus(builder =>
{
    var assembly = typeof(Program).Assembly;

    builder.AddMessaging(messaging =>
        messaging.Contracts.Register<OrderPlaced>("orders.events.placed", 1));
    builder.AddCommands(c => c.RegisterFromAssembly(assembly));
    builder.AddQueries(q => q.RegisterFromAssembly(assembly));
    builder.AddEvents(e => e.RegisterFromAssembly(assembly));

    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UseInMemoryStorage();
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(2));
    });

    builder.AddOutbox(outbox =>
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
| Run command now | `ICommandMediator.SendAsync` | `LiteBus.Commands` |
| Publish event now | `IEventMediator.PublishAsync` | `LiteBus.Events` |

Only `ICommand` (no result) may be stored in the inbox. Analyzer LB1004 flags `ICommand<TResult>` at compile time.

## Root Transport Composition

Register horizontal transport infrastructure once on `ILiteBusBuilder`. Dispatch and ingress extensions require and share it. Register only one broker transport per composition.

| Extension | NuGet package |
| --- | --- |
| `AddAmqpTransport(...)` | `LiteBus.Transport.Amqp` |
| `AddKafkaTransport(...)` | `LiteBus.Transport.Kafka` |
| `AddAwsSqsTransport(...)` | `LiteBus.Transport.AwsSqs` |
| `AddAzureServiceBusTransport(...)` | `LiteBus.Transport.AzureServiceBus` |
| `AddInMemoryTransport()` | `LiteBus.Transport.InMemory` |

## Inbox Composition (`Use*` Extensions)

| Extension | NuGet package |
| --- | --- |
| `UseInMemoryStorage()` | `LiteBus.Inbox.Storage.InMemory` |
| `UsePostgreSqlStorage(...)` | `LiteBus.Inbox.Storage.PostgreSql` |
| `UseEntityFrameworkCoreStorage(...)` | `LiteBus.Inbox.Storage.EntityFrameworkCore` |
| `UseInProcessDispatch()` | `LiteBus.Inbox.Dispatch.InProcess` |
| `UseAmqpDispatch(...)` | `LiteBus.Inbox.Dispatch.Amqp` |
| `UseAzureServiceBusDispatch(...)` | `LiteBus.Inbox.Dispatch.AzureServiceBus` |
| `UseAwsSqsDispatch(...)` | `LiteBus.Inbox.Dispatch.AwsSqs` |
| `UseKafkaDispatch(...)` | `LiteBus.Inbox.Dispatch.Kafka` |
| `UseInMemoryDispatch(...)` | `LiteBus.Inbox.Dispatch.InMemory` |
| `UseAmqpIngress(...)` | `LiteBus.Inbox.Ingress.Amqp` |
| `UseAzureServiceBusIngress(...)` | `LiteBus.Inbox.Ingress.AzureServiceBus` |
| `UseAwsSqsIngress(...)` | `LiteBus.Inbox.Ingress.AwsSqs` |
| `UseKafkaIngress(...)` | `LiteBus.Inbox.Ingress.Kafka` |
| `UseInMemoryIngress(...)` | `LiteBus.Inbox.Ingress.InMemory` |
| `EnableInboxProcessor(...)` | `LiteBus.Inbox` (registers `InboxProcessorBackgroundService` through the manifest) |
| `EnableSaga(...)` | `LiteBus.Saga.InboxIntegration` |

## Outbox Composition (`Use*` Extensions)

| Extension | NuGet package |
| --- | --- |
| `UseInMemoryStorage()` | `LiteBus.Outbox.Storage.InMemory` |
| `UsePostgreSqlStorage(...)` | `LiteBus.Outbox.Storage.PostgreSql` |
| `UseEntityFrameworkCoreStorage(...)` | `LiteBus.Outbox.Storage.EntityFrameworkCore` |
| `UseInProcessDispatch()` | `LiteBus.Outbox.Dispatch.InProcess` |
| `UseAmqpDispatch(...)` | `LiteBus.Outbox.Dispatch.Amqp` |
| `UseAzureServiceBusDispatch(...)` | `LiteBus.Outbox.Dispatch.AzureServiceBus` |
| `UseAwsSqsDispatch(...)` | `LiteBus.Outbox.Dispatch.AwsSqs` |
| `UseKafkaDispatch(...)` | `LiteBus.Outbox.Dispatch.Kafka` |
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

The receipt confirms storage, not a business result. The processor runs or publishes the envelope later under a lease.

## PostgreSQL Storage

```csharp
builder.AddInbox(inbox =>
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

Inbox, outbox, and saga use PostgreSQL schema version 1. The version 1 scripts hold the full current schema. See the [Migration Guide v6](../migration/v6.md) for historical table transitions.

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

Register OpenTelemetry for each axis you export:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddLiteBusInboxMetrics()
        .AddLiteBusOutboxMetrics()
        .AddLiteBusTransportMetrics());
```

Instrument names on `LiteBusInboxTelemetry`, `LiteBusOutboxTelemetry`, and `LiteBusTransportTelemetry` are a public contract. Renaming them is a breaking change.

## Processor Options

| Option | Default | Notes |
| --- | --- | --- |
| `HonorShutdownTokenOnPersist` | `false` | When `false`, terminal `PersistAsync` uses `CancellationToken.None`, which is safer against duplicate dispatch but may block shutdown. When `true`, it passes the shutdown token, which drains faster but risks duplicate dispatch. |
| `DispatcherConcurrency` | `1` | Raise only when handlers are safe in parallel. |
| `LeaseHeartbeatInterval` | 15s | Must be `<= LeaseDuration / 2`. |

## Naming Schemes

| Form | Meaning |
| --- | --- |
| Folder / project `LiteBus.Inbox.Storage.PostgreSql` | Source layout |
| Namespace `LiteBus.Inbox.Storage.PostgreSql` | C# API |
| NuGet `LiteBus.Inbox.Storage.PostgreSql` | Package ID |

## Next

- [Getting Started](README.md) for the full walkthrough
- [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) for durable messaging depth
- [Dependency Graph](../architecture/dependency-graph.md) for package boundaries
