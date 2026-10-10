# Outbox

The outbox stores events for publication after a state change commits, so a message is never published for a change that rolled back and never lost for a change that committed. This page is the contract and registration reference for the neutral outbox core.

> **This page is for** developers who publish events that must survive a process crash.
>
> **After reading it** you can register contracts, storage, dispatch, and the processor for reliable publication. For the durable overview, start with [Reliable Messaging](README.md).

If your command handler must enqueue events in the same database transaction as domain persistence, read [Transactional messaging writes](transactional-writes.md) before choosing between `IOutbox` and `ITransactionalOutbox`.

## Core Module

`LiteBus.Outbox` is transport-neutral orchestration only. It registers:

| Service | Implementation | Role |
| --- | --- | --- |
| `IOutbox` | `Outbox` | Enqueue and serialize messages into storage |
| `IOutboxProcessor` | `PipelinedOutboxProcessor` | Lease due messages, dispatch, record retry or dead-letter state |
| `OutboxProcessorOptions` | options instance | Batch size, lease duration, retry policy |

Storage and dispatch register inside `AddOutbox` through `Use*` extensions.

## Contract

Use `IOutbox.EnqueueAsync` when a message must survive process failure and be published after the surrounding state change commits.

```csharp
public sealed record OrderSubmittedIntegrationEvent
{
    public required Guid OrderId { get; init; }
}

var receipt = await outbox.EnqueueAsync(
    OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.From(
        new OrderSubmittedIntegrationEvent { OrderId = orderId },
        OutboxEnqueueMetadata.Immediate with
        {
            Identity = new MessageIdentity.Supplied(messageId),
            Target = new PublicationTarget.Topic("orders"),
            Trace = new MessageTrace.Correlated(correlationId),
        }),
    cancellationToken);
```

`receipt.Outcome` is `Enqueued` when the store inserted a row and `AlreadyEnqueued` when a duplicate message ID or tenant-scoped idempotency key resolved to an existing row. The receipt confirms storage, not broker publication.

Register each stored message type in `IMessageContractRegistry` with a stable name and version, or apply `[MessageContract]` and call `RegisterFromAssembly` during module configuration.

## Registration (Nested Builder Only)

Register contracts, storage, dispatch, and the processor inside one `AddOutbox` call:

```csharp
services.AddLiteBus(builder =>
{
    builder.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<OrderSubmittedIntegrationEvent>(
            name: "orders.events.order-submitted",
            version: 1);

        outbox.UseProcessorOptions(new OutboxProcessorOptions { BatchSize = 100 });

        outbox.UseInMemoryStorage();
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
    });
});
```

## Processing Versus Publishing

Inbox and outbox use asymmetric naming on purpose. Inbox acceptance (`AcceptAsync`) only stores a command, and processing happens later in `PipelinedInboxProcessor`. Outbox enqueue (`EnqueueAsync`) only stores an event, and publication happens later in `PipelinedOutboxProcessor` when a dispatcher calls `IEventMediator` or an external broker. Do not call `PublishAsync` directly for cross-process notifications that must commit with application state.

## Dispatch (`Use*` Extensions)

| Extension | Package | Behavior |
| --- | --- | --- |
| `UseInProcessDispatch()` | `LiteBus.Outbox.Dispatch.InProcess` | Deserialize and `IEventMediator.PublishAsync` |
| `UseAmqpDispatch(...)` | `LiteBus.Outbox.Dispatch.Amqp` | Publish through the root AMQP transport with contract headers |
| `UseAzureServiceBusDispatch(...)` | `LiteBus.Outbox.Dispatch.AzureServiceBus` | Publish through the root Azure Service Bus transport |
| `UseAwsSqsDispatch(...)` | `LiteBus.Outbox.Dispatch.AwsSqs` | Publish through the root Amazon SQS transport |
| `UseKafkaDispatch(...)` | `LiteBus.Outbox.Dispatch.Kafka` | Publish through the root Kafka transport |
| `UseInMemoryDispatch(...)` | `LiteBus.Outbox.Dispatch.InMemory` | Publish through in-memory transport (tests, local pipelines) |

Register exactly one dispatcher per outbox module.

## Tenant Routing and Contract Resolution

Set an `ITenantRoutingStrategy` on the transport dispatcher options to compute the transport route from tenant metadata before topic and route delegate fallbacks. The strategy is a transport concern, so it lives on `TransportOutboxDispatcherOptions` in `LiteBus.Outbox.Dispatch` rather than on the outbox builder:

```csharp
outbox.UseAmqpDispatch(options => options.TenantRoutingStrategy = new TenantRouteStrategy());
```

The strategy receives the envelope tenant id, contract name, and topic hint, and its return value becomes the transport publish route. When no strategy is registered, the dispatcher uses the envelope topic when present, otherwise `ResolveRoute` when set, otherwise the contract name.

Register an `IMessageContractResolver` to select the CLR type used for contract lookup during enqueue instead of the runtime instance type:

```csharp
outbox.UseContractResolver(new DeclaredTypeMessageContractResolver());
```

When no resolver is registered, writers use the runtime message instance type for contract lookup.

## Storage (`Use*` Extensions)

| Extension | Package |
| --- | --- |
| `UseInMemoryStorage()` | `LiteBus.Outbox.Storage.InMemory` |
| `UsePostgreSqlStorage(...)` | `LiteBus.Outbox.Storage.PostgreSql` |
| `UseEntityFrameworkCoreStorage(...)` | `LiteBus.Outbox.Storage.EntityFrameworkCore` |

### In-Memory Store

Process-local store for unit tests and local development. Thread-safe within one process. Register a dispatcher before `EnableOutboxProcessor()`.

### PostgreSQL Store

```csharp
builder.AddAmqpTransport(new AmqpConnectionOptions
{
    Uri = new Uri(configuration.GetConnectionString("Amqp")!)
});

builder.AddOutbox(outbox =>
{
    outbox.Contracts.Register<OrderSubmittedIntegrationEvent>(
        "orders.events.order-submitted",
        version: 1);

    outbox.UsePostgreSqlStorage(pg =>
    {
        pg.UseDataSource(dataSource);
        pg.EnsureSchemaCreationOnStartup(); // development only
    });

    outbox.UseAmqpDispatch(o => o.DefaultDestination = "orders.events");

    outbox.EnableOutboxProcessor();
});
```

PostgreSQL outbox schema version **1** stores opaque payload text and includes `lease_generation`. The schema manager requires the complete current shape. See [PostgreSQL Schema Management](../integrations/postgresql-schema-management.md) for creation and validation.

Pass the same `NpgsqlDataSource` instance to inbox and outbox when both use one database. See [Transactional messaging writes](transactional-writes.md).

### Transactional Writes (Domain + Outbox)

Default `IOutbox.EnqueueAsync` commits immediately through the singleton store. It does not join an open domain transaction.

| Situation | API |
| --- | --- |
| Ingress, tools, fire-and-forget enqueue | `IOutbox` |
| Command handler plus EF `DbContext` | `ITransactionalOutbox<TContext>`: [Outbox EF Core storage](../integrations/outbox-ef-core-storage.md) |
| Command handler plus PostgreSQL (Marten, Dapper, ADO.NET) | `ITransactionalOutbox`: [Transactional messaging writes](transactional-writes.md) |

EF registration, interceptor setup, and duplicate-idempotency behavior on the transactional path live in [Outbox Entity Framework Core Storage](../integrations/outbox-ef-core-storage.md).

## Delivery Semantics

Outbox publication is at-least-once. `PipelinedOutboxProcessor` dispatches through `IOutboxDispatcher` before it persists terminal published state. A crash or `PersistAsync` failure after a successful broker publish leaves the row leased or pending and the processor retries, which can duplicate publication downstream. Consumers must deduplicate or handle retries idempotently (for example with `MessageId`, `IdempotencyKey`, or broker deduplication headers).

LiteBus does not implement a two-phase publish acknowledgment in which a broker acknowledgement token is persisted before terminal state. Treat external side effects as idempotent, or wrap dispatch in a custom `IOutboxDispatcher` that records an outbox-side acknowledgement before returning success.

The integration test `ProcessPendingAsync_WhenPersistSkippedAfterPublish_ShouldRepublishOnRetry` in `LiteBus.Outbox.Dispatch.InMemory.IntegrationTests` demonstrates the crash window: a simulated persist skip after transport publish causes a second publish when the lease is reclaimed.

`HonorShutdownTokenOnPersist` on `OutboxProcessorOptions` mirrors the inbox option (default `false` uses `CancellationToken.None` on terminal persist).

## Processing Flow

1. Application code enqueues through `IOutbox.EnqueueAsync` or `ITransactionalOutbox.EnqueueAsync` (when domain and outbox must share one transaction).
2. The writer resolves contract name and version.
3. The writer serializes and stores an outbox envelope.
4. `PipelinedOutboxProcessor` leases due messages.
5. The processor calls `IOutboxDispatcher.DispatchAsync` for each message.
6. The processor marks the message published, failed for retry, or dead-lettered.

## Store Roles

| Interface | Used by | Responsibility |
| --- | --- | --- |
| `IOutboxStore` | `IOutbox` | Append a pending envelope and return `OutboxAppendResult` with the insertion outcome |
| `IOutboxLeaseStore` | `IOutboxProcessor` | Atomically claim due messages |
| `IOutboxStateWriter` | `IOutboxProcessor` | Persist published, failed, or dead-lettered envelopes |
| `IOutboxDeadLetterStore` | Operator tooling | Requeue dead-lettered messages |
| `IOutboxRetentionStore` | Cleanup services | Delete published messages past retention |
| `IOutboxDiagnosticsStore` | Operators | Status counts |

## In-Process Dispatch

`UseInProcessDispatch` replays stored events into the local event pipeline instead of a broker.

```csharp
outbox.UseInProcessDispatch();
```

A dispatched message with no registered handler fails the attempt by default, so it retries and eventually dead-letters rather than being marked `Published`. An immediate `PublishAsync` treats zero subscribers as a silent no-op, which is defensible when the caller can see there are none. A durable dispatch has no such caller, so the two axes deliberately disagree.

The failure this default prevents is silent and total: if the assembly holding the handlers is never scanned by `AddEvents`, every outbox message is leased, dispatched, and marked `Published` without leaving the process, and the backlog reads zero throughout.

```csharp
// Only when publishing to zero handlers is a state the application deliberately allows.
outbox.UseInProcessDispatch(dispatch => dispatch.RequireHandler = false);
```

| Option | Default | Behavior |
| --- | --- | --- |
| `RequireHandler` | `true` | A message with no handler fails the dispatch attempt, retries, and dead-letters |
| `RequireHandler` | `false` | A message with no handler completes and is marked `Published` |

## Retry and Dead Letter

```csharp
outbox.UseProcessorOptions(new OutboxProcessorOptions
{
    BatchSize = 100,
    LeaseDuration = TimeSpan.FromMinutes(2),
    Retry = new RetryOptions
    {
        MaxAttempts = 12,
        InitialDelay = TimeSpan.FromSeconds(10),
        MaxDelay = TimeSpan.FromMinutes(10),
        Backoff = RetryBackoff.Exponential,
        UseJitter = true
    }
});
```

`Retry.MaxAttempts` counts dispatch attempts recorded on the leased envelope. Requeue operations through `IOutboxManager.RequeueAsync` return `RequeueResult { Requested, Requeued }`.

### Explicit Retry Schedules

`RetryBackoff.Fixed` and `RetryBackoff.Exponential` derive every delay from `InitialDelay` under the `MaxDelay` cap. That cannot express a window that starts inside a minute and still reaches days. Exponential from one minute reaches roughly four hours by the ninth attempt. Stretching the tail to a week also pushes the first few retries hours apart.

`RetryBackoff.Schedule` takes the ladder directly:

```csharp
Retry = new RetryOptions
{
    MaxAttempts = 8,
    Backoff = RetryBackoff.Schedule,
    Delays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(8),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(3),
        TimeSpan.FromDays(7)
    ]
}
```

| Rule | Behavior |
| --- | --- |
| Attempt past the end of the ladder | Repeats the last entry rather than failing or wrapping |
| `MaxDelay` | Does not clamp ladder entries; the ladder is the policy |
| `UseJitter` | Still applies, bounded by the ladder entry rather than by `MaxDelay` |
| Empty `Delays` with `Backoff = Schedule` | Refused at composition, not at the first failure |
| `Delays` with any other backoff | Ignored |

`Delays` is validated when the module builds. A misconfigured ladder fails at boot rather than on the first failure.

After-dispatch hook failures follow `HookFailurePolicy` on `OutboxProcessorOptions`. The property defaults to `null`, which keeps the dispatcher recommendation; set an explicit value to override it. Registration order does not matter: a batch-only `UseProcessorOptions` call keeps the transport default.

| Dispatcher | Default `HookFailurePolicy` | Behavior after successful publish |
| --- | --- | --- |
| Transport (`UseAmqpDispatch`, `UseInMemoryDispatch`, and siblings) | `CompleteDespiteHookFailure` | Logs the hook error and persists published state without re-publishing |
| In-process (`UseInProcessDispatch`) | `DeadLetter` | Moves the row to dead letter without re-publishing |

Set `HookFailurePolicy = ProcessorHookFailurePolicy.DeadLetter` on transport dispatchers when hook side effects must block terminal completion. Overriding the transport default dead-letters a message the broker already accepted. The message is re-leased after replay and published a second time. Set `HookFailurePolicy = ProcessorHookFailurePolicy.CompleteDespiteHookFailure` on in-process dispatch when hook persistence should not undo a successful handler publication.

Transport dispatchers (`UseAmqpDispatch`, `UseAwsSqsDispatch`, and siblings) accept `ValidatePayloadBeforeDispatch` on their options. When `false` (default), invalid JSON is not deserialized before publish; when `true`, deserialization runs before the transport call so corrupt payloads fail fast without broker side effects.

Batch enqueue serializes items in parallel inside `OutboxEnvelopeFactory.CreateBatchAsync`.

## Next

- Atomic domain plus outbox: [Transactional messaging writes](transactional-writes.md)
- Domain events: [Domain events and unit of work](../concepts/domain-events-and-unit-of-work.md)
- Custom storage: [Custom stores and dispatchers](../extending/custom-stores-and-dispatchers.md)
