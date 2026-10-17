# Inbox

The inbox stores commands for deferred execution by a background processor, so work accepted at the edge survives a process crash and runs at least once. This page is the contract and registration reference for the core inbox module.

> **This page is for** developers who accept commands that must survive a process crash.
>
> **After reading it** you can register contracts, storage, dispatch, and the processor for deferred execution. For the durable overview, start with [Reliable Messaging](README.md).

If acceptance must happen in the same database transaction as domain persistence, see [Transactional messaging writes](transactional-writes.md).

## Contract

Use `IInbox.AcceptAsync` when the caller should receive an acceptance receipt instead of waiting for handler execution. The inbox is a durable command queue: acceptance stores work, and `PipelinedInboxProcessor` executes it with at-least-once semantics.

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
    cancellationToken).ConfigureAwait(false);
```

The receipt confirms acceptance into the inbox. It does not carry the business result from the message handler.

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

When `Outcome` is `AlreadyAccepted`, the store returned an existing row for the supplied message id or tenant-scoped idempotency key. Deduplication applies regardless of row status: a repeat accept with the same key returns the stored envelope, including `Completed` rows, without re-executing the handler.

Under `IdempotencyConflictMode.Strict`, an identical replay still returns the existing row; only a conflicting submission whose id, contract name and version, payload, idempotency key, correlation, causation, or tenant differs throws `IdempotencyConflictException`.

Start from `InboxAcceptMetadata.Immediate with { ... }` (or the factories below) rather than building metadata with uninitialized required fields.

Use a query or read model when a caller needs the final state later. Deferred execution runs through `InboxAcceptMetadata.Visibility` (`MessageVisibility.At` or `MessageVisibility.After`). The writer surface has no separate scheduler interface.

### Item Factories

`InboxAcceptItem<TMessage>` carries static factories for common metadata shapes. Each factory starts from `InboxAcceptMetadata.Immediate` and sets one concern:

| Factory | Sets |
| --- | --- |
| `From(message)` | Default metadata |
| `From(message, metadata)` | Caller-supplied metadata |
| `ScheduledAt(message, visibleAfter)` | `MessageVisibility.At`: lease after an absolute UTC timestamp |
| `ScheduledAfter(message, delay)` | `MessageVisibility.After`: lease after a relative delay |
| `WithIdempotency(message, key)` | `Idempotency.Keyed`: insert-time deduplication key |
| `WithIdentity(message, messageId)` | `MessageIdentity.Supplied`: caller-owned message id |
| `WithCorrelation(message, correlationId)` | `MessageTrace.Correlated`: workflow correlation id |
| `WithTrace(message, trace)` | Caller-supplied `MessageTrace` variant |
| `WithTenant(message, tenantId)` | `TenantScope.Isolated`: tenant partition |

```csharp
var item = InboxAcceptItem<ProcessPaymentCommand>.ScheduledAfter(
    new ProcessPaymentCommand(paymentId, amount),
    TimeSpan.FromMinutes(30));

var receipt = await inbox.AcceptAsync(item, cancellationToken).ConfigureAwait(false);
```

Compose concerns with `with` on the metadata record when one factory is not enough, as the lead example shows.

### Batch Writers

`AcceptBatchAsync` accepts heterogeneous messages in one store round trip. Build each entry with the untyped `InboxAcceptItem`, which carries an explicit runtime type for contract lookup. Receipts return in the same order as the input items.

```csharp
IReadOnlyList<InboxAcceptItem> items =
[
    InboxAcceptItem.From(new ProcessPaymentCommand(paymentId, amount), typeof(ProcessPaymentCommand)),
    InboxAcceptItem.From(new NotifyCustomerCommand(customerId), typeof(NotifyCustomerCommand)),
];

IReadOnlyList<InboxReceipt> receipts = await inbox.AcceptBatchAsync(items, cancellationToken).ConfigureAwait(false);
```

`ITransactionalInbox` and `ITransactionalInbox<TContext>` expose the same `AcceptBatchAsync` shape; on those paths the batch stages inside the caller transaction and commits atomically with it. See [Transactional messaging writes](transactional-writes.md).

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

Register every stored message type in `IMessageContractRegistry` with a stable name and version. Register explicitly on the inbox or messaging builder, or use `RegisterFromAssembly`. Analyzers split contract coverage: **LB1007** warns on handled durable types without `[MessageContract]` or registration, and **LB1017** warns on `[MessageContract]` types without explicit `Contracts.Register` or assembly scan. See [Analyzers](../reference/analyzers.md).

Closed generic types work when each closed shape is registered. Open generic contracts are rejected.

## Processor Options

`InboxProcessorOptions` inherits the shared durable settings. The processor reads them when the processor instance is created.

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
| `HookFailurePolicy` | `DeadLetter` | Inbox hardcodes dead letter when dispatch succeeded but an after-dispatch hook threw |

`LeaseHeartbeatInterval` must be less than or equal to half of `LeaseDuration`; processor construction throws `ArgumentOutOfRangeException` otherwise. Larger batches reduce store round trips but hold more leases during dispatch. Longer leases tolerate slow handlers but delay recovery after a worker crash. See [Ordering and Concurrency Guarantees](ordering.md) before raising `DispatcherConcurrency`.

## Processor Host Options

`EnableInboxProcessor` registers the background loop on the generic host. Its options control lifecycle and polling only:

| Option | Default | Role |
| --- | --- | --- |
| `Enabled` | `true` | Whether the processor loop runs |
| `PollInterval` | 1 second | Delay between poll cycles when the previous batch was empty or partial |
| `StartupDelay` | Zero | Delay after host start before the first poll cycle |
| `UseAdaptivePolling` | `true` | Skip the poll delay after a full batch |

```csharp
inbox.EnableInboxProcessor(host =>
{
    host.StartupDelay = TimeSpan.FromSeconds(10);
    host.UseAdaptivePolling = false;
    host.PollInterval = TimeSpan.FromSeconds(5);
});
```

See [Hosted services](../architecture/hosted-services.md) for how processor loops bind to the generic host.

## Retention Cleanup

Completed rows accumulate until deleted. `EnableCleanup` registers a background loop that deletes completed rows older than the retention period:

```csharp
inbox.EnableCleanup(cleanup =>
{
    cleanup.Interval = TimeSpan.FromHours(6);
    cleanup.Retention = TimeSpan.FromDays(7);
});
```

| Option | Default | Role |
| --- | --- | --- |
| `Enabled` | `true` | Whether the cleanup loop runs |
| `Interval` | 1 hour | Delay between cleanup passes; must be greater than zero |
| `Retention` | `null` | How long completed rows are kept; `null` disables deletion |

When `Retention` is `null`, the loop exits without deleting rows, so calling `EnableCleanup` without setting `Retention` retains everything. `IInboxManager.RunRetentionPurgeAsync` runs the same purge on demand and returns the deleted row count; it returns `0` when retention is unset. `IInboxManager.GetRetentionStatusAsync` reports the configured policy and the most recent cleanup outcome.

## Storage (`Use*` Extensions)

| Extension | Package |
| --- | --- |
| `UseInMemoryStorage()` | `LiteBus.Inbox.Storage.InMemory` |
| `UsePostgreSqlStorage(...)` | `LiteBus.Inbox.Storage.PostgreSql` |
| `UseEntityFrameworkCoreStorage(...)` | `LiteBus.Inbox.Storage.EntityFrameworkCore` |

The writer persists through `IInboxStore`. `AddAsync` returns `InboxAppendResult`, which carries the stored envelope and the authoritative `InboxAcceptOutcome`. The processor leases through `IInboxLeaseStore` and records results through `IInboxStateWriter`. One store implementation registers all roles on a single singleton instance.

### Schema Version 1

PostgreSQL inbox schema version **1** stores opaque payload text and includes `lease_generation`. The schema manager requires the complete current shape. Historical table replacement and data-mapping procedures live in the [Migration Guide](../migration/v6.md).

### Transactional Writes (Domain + Inbox)

Default `IInbox.AcceptAsync` commits immediately through the singleton store. Broker ingress uses this path.

| Situation | API |
| --- | --- |
| Ingress, edge accept, standalone storage | `IInbox` |
| Command handler plus EF `DbContext` | `ITransactionalInbox<TContext>`: [Inbox EF Core storage](../integrations/inbox-ef-core-storage.md) |
| Command handler plus PostgreSQL (Marten, Dapper, ADO.NET) | `ITransactionalInbox`: [Transactional messaging writes](transactional-writes.md) |

On the transactional EF path, a duplicate `message_id` or `(tenant_id, idempotency_key)` fails `SaveChanges` and rolls back the whole unit of work. Sharing a `DbContext` transaction gives no silent idempotent accept.

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

Broker ingress extensions (`UseAmqpIngress`, `UseAzureServiceBusIngress`, `UseAwsSqsIngress`, `UseKafkaIngress`, `UseInMemoryIngress`) consume messages into `IInbox.AcceptAsync`. Register the matching `Add*Transport(...)` extension once at the root; ingress does not own broker connectivity.

The accept-path contract is the same on every broker: ingress maps the broker delivery id to `MessageIdentity.Supplied` and a broker-scoped `Idempotency.Keyed` value (`ingress:{destination}:{brokerMessageId}`), so broker redelivery after a successful store accept does not create duplicate inbox rows. Ingress honors `litebus-visible-after` (absolute ISO-8601) and `litebus-visible-after-delay` (relative `TimeSpan` or tick count); relative delay takes precedence when both headers are present. Application idempotency and tenant headers apply only when the broker binding is authenticated and `Safety.TrustApplicationHeaders` is `true`.

Safety options, prefetch tuning, and acknowledgement policy are broker-specific. See the [AMQP](../integrations/amqp.md), [Kafka](../integrations/kafka.md), [AWS SQS](../integrations/aws-sqs.md), and [Azure Service Bus](../integrations/azure-service-bus.md) guides, and [Reliable Messaging Semantics](semantics.md) for the acknowledgement table.

## Processing Flow

1. Application code accepts a message through `IInbox.AcceptAsync` in the same transaction as the state change when possible.
2. The writer resolves contract name and version from `message.GetType()`.
3. The writer serializes the message and stores an `InboxEnvelope`.
4. `PipelinedInboxProcessor` leases due envelopes.
5. The processor calls `IInboxDispatcher.DispatchAsync` for each envelope inside a per-message DI scope.
6. The processor persists completed, retry, or dead-letter state through `IInboxStateWriter.PersistAsync`.

## Delivery Semantics

Inbox processing is at-least-once, not exactly-once. A handler may run more than once after worker crash, lease expiry, heartbeat loss, or broker redelivery. Use `InboxAcceptMetadata.Idempotency` (`Idempotency.Keyed`) for insert-time deduplication, and write idempotent handlers for side effects.

`HonorShutdownTokenOnPersist` on `InboxProcessorOptions` controls terminal persist cancellation:

| Value | Behavior |
| --- | --- |
| `false` (default) | Terminal `PersistAsync` receives `CancellationToken.None`. Shutdown may block until persist completes; lower risk of duplicate dispatch if persist is interrupted. |
| `true` | Terminal persist receives the shutdown token. Faster drain on host stop; higher risk of duplicate dispatch if persist is canceled mid-write. |

On graceful shutdown or drain, a dispatch the processor cancels is released through `ILeaseRenewable.ReleaseLeaseAsync`. The row returns to `Pending` (or `Failed` when it already carries an error) with its lease cleared, its attempt count decremented, and its visibility unchanged. The cancellation does not spend an attempt. The release runs under a 5 second budget and logs EventId 3011 at Debug. When it fails, EventId 3012 is logged at Warning and the lease expires, so a later pass reclaims the row. When lease heartbeat renewal fails during dispatch, the processor persists a retryable `Failed` outcome instead of leaving the row in `Processing`.

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

`RetryOptions` defaults apply when no retry policy is set:

| Option | Default | Role |
| --- | --- | --- |
| `MaxAttempts` | `5` | Dispatch attempts before the row moves to `DeadLettered`; must be greater than zero |
| `InitialDelay` | 5 seconds | Delay before the first retry |
| `MaxDelay` | 5 minutes | Cap on the calculated delay for `Fixed` and `Exponential` backoff |
| `Backoff` | `Exponential` | `Fixed`, `Exponential`, or `Schedule` |
| `UseJitter` | `true` | Applies a random +-20% factor to the calculated delay |

With the defaults, the four retry delays before jitter are 5, 10, 20, and 40 seconds: each step doubles `InitialDelay` and the 5 minute cap never binds. Jitter multiplies each delay by a factor between 0.8 and 1.2 and the result stays bounded by `MaxDelay`.

`Retry.MaxAttempts` counts dispatch attempts recorded on the leased envelope. When `AttemptCount` reaches `MaxAttempts` on failure, the processor moves the row to `DeadLettered`. Requeue operations through `IInboxManager.RequeueAsync` return `RequeueResult { Requested, Requeued }` so operators can see how many identifiers were not in the dead-letter state. Stores with a database clock use the database clock for lease visibility checks. `InboxLeaseRequest.Now` and `LeaseRenewalRequest.RequestedExpiresAt` are the fallback for stores without one.

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

After-dispatch hook failures default to dead-lettering even when dispatch succeeded. Set `HookFailurePolicy = ProcessorHookFailurePolicy.CompleteDespiteHookFailure` on `InboxProcessorOptions` when hook persistence should not undo a successful handler run. The inbox default applies to transport dispatchers too: inbox AMQP or Kafka dispatch plus a hook failure dead-letters a message the broker already accepted, and the message is re-leased after replay and published a second time.

## In-Memory Store Note

`UseInMemoryStorage()` uses one process-wide lock for store mutations. It fits unit tests and single-process demos, not multi-worker production simulation. Prefer PostgreSQL or EF Core storage when validating lease contention across workers.

## Dual-Hop Topology

Ingress accepts into the inbox store; dispatch adapters publish leased envelopes to a broker for remote execution. Document both hops in runbooks: broker ingress idempotency (stable delivery id) is independent from inbox processor at-least-once execution.

## Processor Lifetime Note

`IInboxProcessor` registers as transient, but `InboxProcessorBackgroundService` holds one processor instance for the process lifetime so `LeaseOwner` stays stable. This is intentional.

## Next

- Registration to processed message: [Durable Messaging Walkthrough](walkthrough.md)
- Atomic domain plus inbox: [Transactional messaging writes](transactional-writes.md)
- Custom storage: [Custom stores and dispatchers](../extending/custom-stores-and-dispatchers.md)
- Hosted workers: [Hosted services](../architecture/hosted-services.md)
