# Outbox

The outbox stores events for publication after a state change commits, so publication is atomic with the committed state change: a rolled-back change publishes nothing, and a committed change keeps its stored event for later publication. This page is the contract and registration reference for the neutral outbox core.

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
    cancellationToken).ConfigureAwait(false);
```

`receipt.Outcome` is `Enqueued` when the store inserted a row and `AlreadyEnqueued` when a duplicate message ID or tenant-scoped idempotency key resolved to an existing row. The receipt confirms storage, not broker publication.

Under `IdempotencyConflictMode.Strict`, an identical replay still returns the existing row; only a conflicting submission whose id, contract, payload, topic, handler, keys, correlation, causation, or tenant differs throws `IdempotencyConflictException`.

Register each stored message type in `IMessageContractRegistry` with a stable name and version, or apply `[MessageContract]` and call `RegisterFromAssembly` during module configuration.

### Item Factories

`OutboxEnqueueItem<TEvent>` carries static factories for common metadata shapes. Each factory starts from `OutboxEnqueueMetadata.Immediate` and sets one concern:

| Factory | Sets |
| --- | --- |
| `From(message)` | Default metadata |
| `From(message, metadata)` | Caller-supplied metadata |
| `ScheduledAt(message, visibleAfter)` | `MessageVisibility.At`: lease after an absolute UTC timestamp |
| `ScheduledAfter(message, delay)` | `MessageVisibility.After`: lease after a relative delay |
| `WithIdempotency(message, key)` | `Idempotency.Keyed`: insert-time deduplication key |
| `WithIdentity(message, messageId)` | `MessageIdentity.Supplied`: caller-owned message id |
| `WithTopic(message, topic)` | `PublicationTarget.Topic`: explicit publication topic |
| `FromAmbient(message)` | Ambient trace: `Workflow` or `Distributed` stamped from the ambient execution context |

```csharp
var item = OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.ScheduledAfter(
    new OrderSubmittedIntegrationEvent { OrderId = orderId },
    TimeSpan.FromMinutes(30));

var receipt = await outbox.EnqueueAsync(item, cancellationToken).ConfigureAwait(false);
```

Compose concerns with `with` on the metadata record when one factory is not enough, as the lead example shows.

### Propagating Trace from the Incoming Message

When a handler runs inside a mediation that replayed a stored envelope, `FromAmbient` stamps the new event with the incoming trace instead of leaving it untraced: the causation id becomes the incoming message id, the correlation id carries over (or falls back to the incoming message id when the incoming envelope had none), and an ambient distributed trace context is preserved.

```csharp
var item = OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.FromAmbient(
    new OrderSubmittedIntegrationEvent { OrderId = orderId });

var receipt = await outbox.EnqueueAsync(item, cancellationToken).ConfigureAwait(false);
```

This is explicit and opt-in: nothing reads ambient state unless the caller calls `FromAmbient`. Outside a mediation the item is identical to `From(message)`, with `Trace` set to `MessageTrace.None`. Only trace metadata is propagated; tenant, visibility, and identity keep their `Immediate` defaults. `InboxAcceptItem<TMessage>.FromAmbient` is the inbox twin for handlers that accept a follow-up command. A `MessageTrace.Distributed` context must be valid JSON text such as `{"traceparent":"00-..."}`; anything else throws `ArgumentException` at creation on every provider, so pass a JSON object instead of a raw traceparent string.

### Ambient Trace Option

`UseAmbientTrace` applies `FromAmbient` semantics to every enqueue whose item trace is `MessageTrace.None`, so handlers that enqueue inside a mediation propagate trace without calling `FromAmbient` on each item.

```csharp
builder.AddOutbox(outbox =>
{
    outbox.UseInMemoryStorage();
    outbox.UseAmbientTrace();
});
```

An explicit item trace always wins over the ambient value: the option only fills in `None`. Outside a mediation the ambient read yields none, so the envelope is identical to one enqueued without the option. Collected domain events staged through `StageCollectedEvents` stamp ambient trace under the same option.

### Publication Targets

`OutboxEnqueueMetadata.Target` selects how dispatchers resolve the publication destination:

| Variant | Meaning |
| --- | --- |
| `PublicationTarget.ContractDefault` | Derive the transport target from the stored contract name and module configuration |
| `PublicationTarget.Topic(name)` | Explicit topic or channel stored with the envelope |
| `PublicationTarget.Exchange(name)` | Explicit AMQP exchange name stored with the envelope |
| `PublicationTarget.Queue(name)` | Explicit AMQP queue name stored with the envelope |

All three explicit variants persist to the envelope topic column at enqueue time; dispatchers resolve the transport destination from that stored hint. `Exchange` and `Queue` name AMQP concepts, so prefer `Topic` on non-AMQP transports. Broker-specific destination mapping lives in the dispatch guides under [Integrations](../integrations/amqp.md).

### Batch Writers

`EnqueueBatchAsync` enqueues heterogeneous events in one store round trip. Build each entry with the untyped `OutboxEnqueueItem`, which carries an explicit runtime type for contract lookup. Receipts return in the same order as the input items.

```csharp
IReadOnlyList<OutboxEnqueueItem> items =
[
    OutboxEnqueueItem.From(
        new OrderSubmittedIntegrationEvent { OrderId = orderId },
        typeof(OrderSubmittedIntegrationEvent)),
    OutboxEnqueueItem.From(
        new CustomerNotifiedIntegrationEvent { CustomerId = customerId },
        typeof(CustomerNotifiedIntegrationEvent)),
];

IReadOnlyList<OutboxReceipt> receipts = await outbox.EnqueueBatchAsync(items, cancellationToken).ConfigureAwait(false);
```

`ITransactionalOutbox` and `ITransactionalOutbox<TContext>` expose the same `EnqueueBatchAsync` shape; on those paths the batch stages inside the caller transaction and commits atomically with it. See [Transactional messaging writes](transactional-writes.md).

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

## Processor Options

`OutboxProcessorOptions` inherits the shared durable settings. The processor reads them when the processor instance is created.

| Option | Default | Role |
| --- | --- | --- |
| `BatchSize` | `20` | Messages leased per processing pass; must be greater than zero |
| `LeaseDuration` | 1 minute | How long a lease lasts before another worker may reclaim the row |
| `DispatcherConcurrency` | `1` | Parallel dispatch workers per pass; keep `1` when arrival order matters |
| `LeaseHeartbeatInterval` | 15 seconds | Lease renewal cadence during dispatch; zero disables renewal |
| `Retry` | `new RetryOptions()` | Retry policy; see [Retry and Dead Letter](#retry-and-dead-letter) |
| `LeaseOwner` | Generated | Owner written on leased rows; defaults to machine name, process id, and a random suffix |
| `TenantId` | `null` | When set, leases only that tenant partition; `null` leases all tenants |
| `HonorShutdownTokenOnPersist` | `false` | Whether terminal persist observes the shutdown token |
| `HookFailurePolicy` | Dispatcher default | `null` keeps the dispatcher recommendation; an explicit value overrides it |

`LeaseHeartbeatInterval` must be less than or equal to half of `LeaseDuration`; processor construction throws `ArgumentOutOfRangeException` otherwise. Larger batches reduce store round trips but hold more leases during dispatch. Longer leases tolerate slow handlers but delay recovery after a worker crash. See [Ordering and Concurrency Guarantees](ordering.md) before raising `DispatcherConcurrency`.

## Processor Host Options

`EnableOutboxProcessor` registers the background loop on the generic host. Its options control lifecycle and polling only:

| Option | Default | Role |
| --- | --- | --- |
| `Enabled` | `true` | Whether the processor loop runs |
| `PollInterval` | 1 second | Delay between poll cycles when the previous batch was empty or partial |
| `StartupDelay` | Zero | Delay after host start before the first poll cycle |
| `UseAdaptivePolling` | `true` | Skip the poll delay after a full batch |

```csharp
outbox.EnableOutboxProcessor(host =>
{
    host.StartupDelay = TimeSpan.FromSeconds(10);
    host.UseAdaptivePolling = false;
    host.PollInterval = TimeSpan.FromSeconds(5);
});
```

See [Hosted services](../architecture/hosted-services.md) for how processor loops bind to the generic host.

## Retention Cleanup

Published rows accumulate until deleted. `EnableCleanup` registers a background loop that deletes published rows older than the retention period:

```csharp
outbox.EnableCleanup(cleanup =>
{
    cleanup.Interval = TimeSpan.FromHours(6);
    cleanup.Retention = TimeSpan.FromDays(7);
});
```

| Option | Default | Role |
| --- | --- | --- |
| `Enabled` | `true` | Whether the cleanup loop runs |
| `Interval` | 1 hour | Delay between cleanup passes; must be greater than zero |
| `Retention` | `null` | How long published rows are kept; `null` disables deletion |

When `Retention` is `null`, the loop exits without deleting rows, so calling `EnableCleanup` without setting `Retention` retains everything. `IOutboxManager.RunRetentionPurgeAsync` runs the same purge on demand and returns the deleted row count; unlike the inbox manager, it throws `OutboxManagementException` when retention is unset. `IOutboxManager.GetRetentionStatusAsync` reports the configured policy and the most recent cleanup outcome.

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

The strategy receives the envelope tenant id, contract name, and topic hint, and its return value becomes the transport publish route. Without a strategy, the dispatcher uses `ResolveRoute` when set, otherwise the envelope topic when present, otherwise the contract name.

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

Process-local store for unit tests and local development. Thread-safe within one process. Register storage and a dispatcher before `EnableOutboxProcessor()`; module build fails when the processor is enabled without a dispatcher.

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
    });

    outbox.UseAmqpDispatch(o => o.DefaultDestination = "orders.events");

    outbox.EnableOutboxProcessor();
});
```

`EnsureSchemaCreationOnStartup` defaults to `true`, so a plain `UsePostgreSqlStorage` call creates schema at startup. Migration-owned production deployments must set it to `false` via `UseOptions` and keep `ValidateSchemaCreationOnStartup` (`true`) so startup validates instead of creating. See [PostgreSQL Schema Management](../integrations/postgresql-schema-management.md) for creation and validation.

PostgreSQL outbox schema version **1** stores opaque payload text and includes `lease_generation` and the nullable `handler` target used by per-handler delivery. The schema manager requires the complete current shape.

Pass the same `NpgsqlDataSource` instance to inbox and outbox when both use one database. See [Transactional messaging writes](transactional-writes.md).

### Transactional Writes (Domain + Outbox)

Default `IOutbox.EnqueueAsync` commits immediately through the singleton store. It does not join an open domain transaction.

| Situation | API |
| --- | --- |
| Ingress, tools, fire-and-forget enqueue | `IOutbox` |
| Command handler plus EF `DbContext` | `ITransactionalOutbox<TContext>`: [Outbox EF Core storage](../integrations/outbox-ef-core-storage.md) |
| Command handler plus PostgreSQL (Marten, Dapper, ADO.NET) | `ITransactionalOutbox`: [Transactional messaging writes](transactional-writes.md) |

### Domain Event Staging

`StageCollectedEvents` registers the default scoped `IDomainEventCollector` and a completion handler that drains the collector into `ITransactionalOutbox` when a mediation completes successfully, ordered at `HandlerPriorities.Persistence` before the application's commit at `HandlerPriorities.UnitOfWork`, so staged rows join the unit of work that produced them.

```csharp
builder.AddOutbox(outbox =>
{
    outbox.Contracts.Register<OrderPlacedIntegrationEvent>("orders.events.placed", 1);
    outbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
    outbox.StageCollectedEvents();
});

// In the command handler, after the aggregate change:
collector.Collect(new OrderPlacedIntegrationEvent { OrderId = order.Id });
```

Staging runs only on `MediationOutcome.Succeeded`: a failed mediation stages nothing. The staging handler resolves `ITransactionalOutbox` lazily and only when the drain is non-empty, so mediations that collect nothing never touch it. The application must register a scoped non-generic `ITransactionalOutbox` (the ambient PostgreSQL provider, the Entity Framework Core interceptor adapter, or the Marten writer); collecting events without one fails staging with a descriptive error. Entity Framework Core registers the non-generic adapter over `ITransactionalOutbox<TContext>` only when `EnableSaveChangesInterceptor()` is set. When `UseAmbientTrace` is also enabled, staged envelopes stamp ambient trace. See [Domain events and unit of work](domain-events-and-unit-of-work.md) for the aggregate and transaction-boundary pattern.

EF registration, interceptor setup, and duplicate-idempotency behavior on the transactional path live in [Outbox Entity Framework Core Storage](../integrations/outbox-ef-core-storage.md).

## Delivery Semantics

Outbox publication is at-least-once. `PipelinedOutboxProcessor` dispatches through `IOutboxDispatcher` before it persists terminal published state. A crash or `PersistAsync` failure after a successful broker publish leaves the row leased or pending and the processor retries, which can duplicate publication downstream. Consumers must deduplicate or handle retries idempotently (for example with `MessageId`, `IdempotencyKey`, or broker deduplication headers).

LiteBus does not implement a two-phase publish acknowledgment in which a broker acknowledgement token is persisted before terminal state. Treat external side effects as idempotent, or wrap dispatch in a custom `IOutboxDispatcher` that records an outbox-side acknowledgement before returning success.

A crash between broker publish and terminal persist therefore re-publishes on retry (the `tests/LiteBus.Durable.IntegrationTests/Dispatch/Outbox/InMemory` suite covers this path).

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

The default fails the attempt when no handler is registered: a durable dispatch with no subscriber would otherwise mark the row `Published` without effect. With `RequireHandler = false`, if the assembly holding the handlers is never scanned by `AddEvents`, every outbox message is leased, dispatched, and marked `Published` without leaving the process, and the backlog reads zero throughout.

```csharp
// Only when publishing to zero handlers is a state the application deliberately allows.
outbox.UseInProcessDispatch(dispatch => dispatch.RequireHandler = false);
```

| Option | Default | Behavior |
| --- | --- | --- |
| `RequireHandler` | `true` | A message with no handler fails the dispatch attempt, retries, and dead-letters |
| `RequireHandler` | `false` | A message with no handler completes and is marked `Published` |

### Per-Handler Delivery

`EnablePerHandlerDelivery` writes one envelope per subscribed main handler of each enqueued event instead of one envelope per event, so each reaction gets its own retry, dead letter, and replay: a failing reaction retries alone while the healthy reactions publish.

```csharp
builder.AddOutbox(outbox =>
{
    outbox.UseInMemoryStorage();
    outbox.UseInProcessDispatch();
    outbox.EnablePerHandlerDelivery();
});
```

Each envelope records the full name of the handler it is addressed to in the `Handler` column, and dispatch publishes each envelope to that main handler alone while pipeline observers (pre-stage, post, error, and completion stages) still run on every targeted dispatch. Enqueueing an event with no subscribed main handler throws `NoHandlerFoundException`, matching the default `RequireHandler` rule. A caller idempotency key is derived per handler as `{key}:{handler}`, and a caller-supplied message identifier derives deterministic per-handler identifiers, so a retried enqueue resolves to the same rows. The whole fan-out of one enqueue persists in one batch call, so a mid-fan-out failure never leaves a partial set behind. The returned receipt describes the first per-handler envelope in handler-name order; batch receipts flatten one per envelope in item order.

The trade-off is storage: one row per handler instead of one row per event. A handler added after enqueue does not receive already-enqueued events, and a handler removed before dispatch leaves its envelopes with no matching subscriber, which `RequireHandler` then decides. Use per-handler delivery only with in-process dispatch: broker dispatchers publish the full event per envelope, so per-handler envelopes would duplicate broker publication. Composition throws `DurableStorageConfigurationException` when per-handler delivery meets a transport dispatcher unless `AllowPerHandlerDeliveryWithTransportDispatch` acknowledges the duplication.

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

`RetryOptions` defaults apply when no retry policy is set:

| Option | Default | Role |
| --- | --- | --- |
| `MaxAttempts` | `5` | Dispatch attempts before the row moves to `DeadLettered`; must be greater than zero |
| `InitialDelay` | 5 seconds | Delay before the first retry |
| `MaxDelay` | 5 minutes | Cap on the calculated delay for `Fixed` and `Exponential` backoff |
| `Backoff` | `Exponential` | `Fixed`, `Exponential`, or `Schedule` |
| `UseJitter` | `true` | Applies a random +-20% factor to the calculated delay |

With the defaults, the four retry delays before jitter are 5, 10, 20, and 40 seconds: each step doubles `InitialDelay` and the 5 minute cap never binds. Jitter multiplies each delay by a factor between 0.8 and 1.2 and the result stays bounded by `MaxDelay`.

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

Batch enqueue expands items through `IOutboxEnvelopeFactory.CreateManyAsync` in parallel and persists the flattened envelopes in one batch.

## Next

- Registration to processed message: [Durable Messaging Walkthrough](walkthrough.md)
- Atomic domain plus outbox: [Transactional messaging writes](transactional-writes.md)
- Domain events: [Domain events and unit of work](domain-events-and-unit-of-work.md)
- Custom storage: [Custom stores and dispatchers](../extending/custom-stores-and-dispatchers.md)
