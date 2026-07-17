# Inbox

The inbox stores messages for deferred execution by a background processor, so work accepted at the edge survives a process crash and runs at-least-once with idempotency. This page is the contract and registration reference for the core inbox module. Read [Reliable Messaging](README.md) first.

If acceptance must happen in the **same database transaction** as domain persistence (uncommon outside command handlers), see [Transactional messaging writes](transactional-writes.md).

## Contract

Use `IInbox.AcceptAsync` when the caller should receive an acceptance receipt instead of waiting for handler execution. The inbox is a durable command queue: acceptance stores work; `PipelinedInboxProcessor` executes it with at-least-once semantics.

```csharp
public sealed record ProcessPaymentCommand(Guid PaymentId, decimal Amount) : ICommand;

var receipt = await inbox.AcceptAsync(
    InboxAcceptItem<ProcessPaymentCommand>.From(
        new ProcessPaymentCommand(paymentId, amount),
        InboxAcceptMetadata.Immediate with
        {
            Idempotency = new Idempotency.Keyed($"payment:{paymentId}"),
            Trace = new MessageTrace.Correlated(correlationId),
        }),
    cancellationToken);
```

The receipt confirms acceptance into the inbox. It does not contain the business result from the message handler.

```csharp
public sealed record InboxReceipt
{
    public required Guid Id { get; init; }
    public required Type MessageType { get; init; }
    public required MessageContractReference Contract { get; init; }
    public required DateTimeOffset AcceptedAt { get; init; }
    public required MessageTrace Trace { get; init; }
    public required TenantScope Tenant { get; init; }
    public InboxAcceptOutcome Outcome { get; init; } = InboxAcceptOutcome.Accepted;
}
```

When `Outcome` is `AlreadyAccepted`, the store returned an existing row for the supplied message id or tenant-scoped idempotency key. Completed rows remain deduplicated: a repeat accept with the same `(tenant_id, idempotency_key)` pair returns the completed envelope without re-executing the handler.

Use `InboxAcceptMetadata.Immediate with { ... }` (or helpers such as `InboxAcceptItem<T>.WithTrace`, `WithTenant`, `WithCorrelation`) rather than constructing metadata with uninitialized required fields. Heterogeneous batch APIs accept `InboxAcceptItem.From(message, messageType)` when contract lookup must not rely on `message.GetType()` alone.

Use a query or read model when a caller needs the final state later.

Deferred execution is expressed through `InboxAcceptMetadata.Visibility` (`MessageVisibility.At` or `MessageVisibility.After`). There is no separate scheduler interface on the writer surface.

## Core Module

`LiteBus.Inbox` registers orchestration only:

| Service | Implementation | Role |
| --- | --- | --- |
| `IInbox` | `Inbox` | Serialize and store accepted envelopes |
| `IInboxProcessor` | `PipelinedInboxProcessor` | Lease due envelopes, dispatch, record state |
| `InboxProcessorOptions` | configured instance | Batch size, lease duration, retry policy |

Storage, dispatch, and ingress register inside `AddInbox` through `Use*` extensions. The core module does not reference Commands, Events, PostgreSQL, EF Core, or RabbitMQ.

```csharp
services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(c => c.RegisterFromAssembly(typeof(ProcessPaymentCommand).Assembly));

    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>(
            name: "payments.commands.process-payment",
            version: 1);

        inbox.UseProcessorOptions(new InboxProcessorOptions
        {
            BatchSize = 100,
            LeaseDuration = TimeSpan.FromMinutes(2)
        });

        inbox.UseInMemoryStorage();
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
    });
});
```

Register every stored message type in `IMessageContractRegistry` with a stable name and version. Register explicitly on the inbox or messaging builder, or use `RegisterFromAssembly`. Analyzers split contract coverage: **LB1007** warns on handled durable types without `[MessageContract]` or registration; **LB1017** warns on `[MessageContract]` types without explicit `Contracts.Register` or assembly scan. See [Analyzers](../reference/analyzers.md).

Closed generic types are supported when each closed shape is registered. Open generic contracts are rejected.

## Storage (`Use*` Extensions)

| Extension | Package |
| --- | --- |
| `UseInMemoryStorage()` | `LiteBus.Inbox.Storage.InMemory` |
| `UsePostgreSqlStorage(...)` | `LiteBus.Inbox.Storage.PostgreSql` |
| `UseEntityFrameworkCoreStorage(...)` | `LiteBus.Inbox.Storage.EntityFrameworkCore` |

The writer persists through `IInboxStore`. `AddAsync` returns `InboxAppendResult`, which carries the stored envelope and the authoritative `InboxAcceptOutcome`. The processor leases through `IInboxLeaseStore` and records results through `IInboxStateWriter`. A single store implementation registers all roles on one singleton instance.

### Schema v1 (Greenfield)

PostgreSQL inbox schema version **3** stores opaque payload text and adds `lease_generation`. Apply the v2 and v3 SQL files to an existing v6 table before startup. EF Core applications own the equivalent migration. Tables from v5 or earlier require replacement or an application-owned data migration.

### Transactional Writes (Domain + Inbox)

Default `IInbox.AcceptAsync` commits immediately through the singleton store. Broker ingress uses this path.

| Situation | API |
| --- | --- |
| Ingress, edge accept, standalone storage | `IInbox` |
| Command handler + EF `DbContext` | `ITransactionalInbox<TContext>`: [Inbox EF Core storage](../integrations/inbox-ef-core-storage.md) |
| Command handler + PostgreSQL (Marten, Dapper, ADO.NET) | `ITransactionalInbox`: [Transactional messaging writes](transactional-writes.md) |

On the transactional EF path, duplicate `message_id` or `(tenant_id, idempotency_key)` fails `SaveChanges` and rolls back the entire unit of work. There is no silent idempotent accept when sharing a `DbContext` transaction.

## Dispatch (`Use*` Extensions)

| Extension | Package | Behavior |
| --- | --- | --- |
| `UseInProcessDispatch()` | `LiteBus.Inbox.Dispatch.InProcess` | Deserialize and `ICommandMediator.SendAsync` |
| `UseAmqpDispatch(...)` | `LiteBus.Inbox.Dispatch.Amqp` | Publish leased envelope through the root AMQP transport |
| `UseAzureServiceBusDispatch(...)` | `LiteBus.Inbox.Dispatch.AzureServiceBus` | Publish through the root Azure Service Bus transport |
| `UseAwsSqsDispatch(...)` | `LiteBus.Inbox.Dispatch.AwsSqs` | Publish through the root Amazon SQS transport |
| `UseKafkaDispatch(...)` | `LiteBus.Inbox.Dispatch.Kafka` | Publish through the root Kafka transport |
| `UseInMemoryDispatch(...)` | `LiteBus.Inbox.Dispatch.InMemory` | Publish through in-memory transport (tests, local pipelines) |

Without a registered dispatcher, module build fails when the processor is enabled.

## Ingress

Broker ingress extensions consume messages into `IInbox.AcceptAsync`: `UseAmqpIngress`, `UseAzureServiceBusIngress`, `UseAwsSqsIngress`, `UseKafkaIngress`, and `UseInMemoryIngress`. Register the matching `Add*Transport(...)` extension once at the root; ingress does not own broker connectivity.

Each broker ingress options record exposes a provider-neutral `Safety` value:

| Option | Default | Role |
| --- | --- | --- |
| `Safety.RequireStableIdentity` | `true` | Reject deliveries without a broker message id unless explicitly disabled |
| `Safety.TrustApplicationHeaders` | `false` | Ignore `litebus-idempotency-key` and `tenant-id` unless the broker binding is authenticated |
| `Safety.MaxMessageBytes` | 4 MiB | Reject oversized bodies before deserialization; zero disables the limit |
| `Safety.AuthorizeDeliveryAsync` | `null` | Optional host callback to reject deliveries before accept |
| `Safety.MaxInFlightMessages` | 32 | Cap concurrent LiteBus handler calls independently from broker receive settings |
| `Safety.EnableBatchAccept` | `false` | Buffer deliveries for batch flush; each delivery is still acknowledged individually |
| `Safety.BatchSize` | 10 | Deliveries accepted in one inbox store batch |
| `Safety.BatchMaxWait` | 200 ms | Flush a partial batch under low traffic |

By default, ingress maps the broker delivery id to `MessageIdentity.Supplied` and a broker-scoped `Idempotency.Keyed` value (`ingress:{destination}:{brokerMessageId}`) so broker redelivery after a successful store accept does not create duplicate inbox rows. Set `Safety.TrustApplicationHeaders` to `true` only when upstream publishers are trusted to supply application idempotency and tenant headers.

Receive tuning is explicit per adapter. AMQP and Azure expose native `PrefetchCount`; SQS exposes `ReceiveBatchSize`; Azure exposes `MaxConcurrentCalls`; Kafka and in-memory ingress expose no receive knob they cannot honor. All five adapters use `Safety.MaxInFlightMessages` for the common LiteBus work cap.

Ingress honors `litebus-visible-after` (absolute ISO-8601) and `litebus-visible-after-delay` (relative `TimeSpan` or tick count). Relative delay takes precedence when both headers are present.

When broker acknowledgement fails after a successful inbox accept, ingress logs and records `ingress.ack_failed_after_accept` without discarding the broker delivery. Idempotency prevents duplicate rows on redelivery.

## Processing Flow

1. Application code accepts a message through `IInbox.AcceptAsync` in the same transaction as the state change when possible.
2. The writer resolves contract name and version from `message.GetType()`.
3. The writer serializes the message and stores an `InboxEnvelope`.
4. `PipelinedInboxProcessor` leases due envelopes.
5. The processor calls `IInboxDispatcher.DispatchAsync` for each envelope inside a per-message DI scope.
6. The processor persists completed, retry, or dead-letter state through `IInboxStateWriter.PersistAsync`.

## Delivery Semantics

Inbox processing is **at-least-once**, not exactly-once. A handler may run more than once after worker crash, lease expiry, heartbeat loss, or broker redelivery. Use `InboxAcceptMetadata.Idempotency` (`Idempotency.Keyed`) for insert-time deduplication; implement idempotent handlers for side effects.

`HonorShutdownTokenOnPersist` on `InboxProcessorOptions` controls terminal persist cancellation:

| Value | Behavior |
| --- | --- |
| `false` (default) | Terminal `PersistAsync` receives `CancellationToken.None`. Shutdown may block until persist completes; lower risk of duplicate dispatch if persist is interrupted. |
| `true` | Terminal persist receives the shutdown token. Faster drain on host stop; higher risk of duplicate dispatch if persist is canceled mid-write. |

On graceful shutdown, in-flight envelopes may remain in `Processing` until the lease expires unless the processor drains or releases leases. When lease heartbeat renewal fails during dispatch, the processor persists a retryable `Failed` outcome instead of leaving the row in `Processing`. Document operator expectations for your deployment.

## Saga Orchestration

Saga integrates through `inbox.EnableSaga(...)` from `LiteBus.Saga.InboxIntegration`. Saga hooks implement `IProcessorEnvelopeHook` from `LiteBus.DurableMessaging.Abstractions`; the inbox core maps `InboxEnvelope` through an adapter. Saga does not reference inbox types on its public surface.

```csharp
inbox.EnableSaga(saga =>
{
    saga.MapState<OrderSagaState>("process-order");
    saga.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
});
```

## Retry and Dead Letter

```csharp
inbox.UseProcessorOptions(new InboxProcessorOptions
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

`Retry.MaxAttempts` counts dispatch attempts recorded on the leased envelope. When `AttemptCount` reaches `MaxAttempts` on failure, the processor moves the row to `DeadLettered`. Requeue operations through `IInboxManager.RequeueAsync` return `RequeueResult { Requested, Requeued }` so operators can see how many identifiers were not in the dead-letter state.

After-dispatch hook failures default to dead-lettering even when dispatch succeeded. Set `HookFailurePolicy = ProcessorHookFailurePolicy.CompleteDespiteHookFailure` on `InboxProcessorOptions` when hook persistence should not undo a successful handler run.

## In-Memory Store Note

`UseInMemoryStorage()` uses one process-wide lock for store mutations. It is suitable for unit tests and single-process demos, not multi-worker production simulation. Prefer PostgreSQL or EF Core storage when validating lease contention across workers.

## Dual-Hop Topology

Ingress accepts into the inbox store; dispatch adapters publish leased envelopes to a broker for remote execution. Document both hops in runbooks: broker ingress idempotency (stable delivery id) is independent from inbox processor at-least-once execution.

## Processor Lifetime Note

`IInboxProcessor` registers as transient, but `InboxProcessorBackgroundService` holds one processor instance for the process lifetime so `LeaseOwner` stays stable. This is intentional.

## Next

- Atomic domain + inbox: [Transactional messaging writes](transactional-writes.md)
- Custom storage: [Custom stores and dispatchers](../extending/custom-stores-and-dispatchers.md)
- Hosted workers: [Hosted services](../architecture/hosted-services.md)
- Schema: [PostgreSQL schema management](../integrations/postgresql-schema-management.md)
