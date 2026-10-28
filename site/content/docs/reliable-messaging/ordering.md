# Ordering and Concurrency Guarantees

LiteBus durable messaging guarantees at-least-once execution under leases, not ordered execution. This page names the exact property or option behind each ordering-related behavior, so callers know what holds, when it holds, and what to build themselves.

This page defines the concurrency properties. [Reliable Messaging Semantics](semantics.md) defines the delivery guarantees, and [Message States](../operations/message-states.md) defines the operator states and alerts. For delivery semantics, start with [Reliable Messaging Semantics](semantics.md). For the full contract, see [Envelope Lifecycle](../catalog/durable-core/envelope-lifecycle.md).

## Guarantee Table

| Guarantee | Provided by | Holds when | Does not hold when |
| --- | --- | --- | --- |
| Lease candidates are claimed oldest-first within one pass | Store lease query (`PostgreSqlInboxStore` orders candidates by `created_at`, `FOR UPDATE SKIP LOCKED`) bounded by `ProcessorOptions.BatchSize` | A single pass leases a batch; the batch reflects creation order at claim time | Two processor instances or two passes race; reclaimed stale leases and newly due retries interleave with fresh rows |
| Dispatches within one batch start in lease order | `ProcessorOptions.DispatcherConcurrency` (default `1`); `PipelinedMessageProcessor<TEnvelope, TOptions>` fans leased envelopes out through a `SemaphoreSlim` of that size | `DispatcherConcurrency` is `1`: worker slots serialize dispatch starts in lease order | `DispatcherConcurrency` is greater than `1`: completions interleave, and a slow first message finishes after faster later ones |
| One tenant's rows are leased by one configured processor | `ProcessorOptions.TenantId`, carried on `InboxLeaseRequest.TenantId` and `OutboxLeaseRequest.TenantId` | The processor sets `TenantId`: it leases only that tenant partition | Across tenants (no cross-tenant order is defined); this is lease isolation, not an ordering key |
| Same-saga messages apply one at a time per host | `SagaProcessorHook` per-correlation gate: a `SemaphoreSlim` keyed by tenant, saga definition, and `CorrelationId` | Messages share one correlation and run on one host process; the gate serializes their dispatch scopes | Messages target different correlations (parallel by design); messages for one correlation run on different hosts |
| Same-saga concurrent saves are detected, not merged | `ISagaStore.SaveAsync` optimistic concurrency on the expected version; conflicts throw `SagaConcurrencyException` | Any host: the loser of a concurrent write gets an exception instead of silently overwriting state | When callers swallow `SagaConcurrencyException` without reloading; dirty-state saves are never retried by the hook |
| Saga completion survives a version race | `SagaProcessorHook` completion retry (up to 3 attempts): reloads and re-completes, treating already-completed as success | Completion intent races a concurrent save | Dirty state plus completion in one dispatch, which the hook refuses with `InvalidOperationException` |
| A redelivered durable message does not re-run saga mutation | `SagaInstance<TState>.LastAppliedMessageId`: `SagaProcessorHook` suppresses dispatch (`ShouldDispatch` false) when the stored row already applied the message id | At-least-once redelivery of an already-applied inbox message | A different message id carrying the same business intent: suppression is per message id, not per business key |
| Resubmitted accepts return the stored row | `IdempotencyKey` with `IdempotencyConflictMode.ReturnExisting` (default) on `InboxEnvelope` and `OutboxEnvelope`; outcomes `InboxAcceptOutcome.AlreadyAccepted` and `OutboxEnqueueOutcome.AlreadyEnqueued` | Same key or message id is resubmitted | Distinct keys for one business intent still create two rows; deduplication happens at accept, and says nothing about execution order |
| Strict conflicts throw only for differing submissions | `IdempotencyConflictMode.Strict`: identical replays return the existing row, differing conflicts throw `IdempotencyConflictException` | The replayed submission matches the stored row on id, contract name and version, payload, topic (outbox), idempotency key, correlation, causation, and tenant | Any compared field differs: the store throws instead of returning the row |
| Handlers of one in-process event run in a defined sequence | `EventExecutionSettings`: `PriorityGroupsConcurrencyMode` and `HandlersWithinSamePriorityConcurrencyMode`, both `ConcurrencyMode.Sequential` by default | In-process mediation with default settings: groups run in priority order, handlers inside a group run in registration order | Either mode is set to `ConcurrencyMode.Parallel`; durable dispatch, where each message is a separate lease |
| Retries become due after their delay | `RetryOptions.CalculateDelay` written to `VisibleAfter` on `AsFailed` | Any retryable failure: the row is invisible until the computed delay passes | Across messages: visibility is per row, so a retry never jumps ahead of or behind another row by design |
| No global cross-worker message order | Nothing provides it | Never relied upon | Any design that assumes FIFO across passes, workers, tenants, or processor instances |
| Broker-native ordering (Kafka partition order, SQS FIFO, Service Bus sessions) | Nothing in LiteBus dispatch configures it | Only where the application configures it outside LiteBus: `TransportPublishRequest` carries `Destination`, `Route`, headers, and ids only, and `TransportOutboxDispatcherOptions` / `TransportInboxDispatcherOptions` expose destination, content type, persistence, routing, and payload validation, with no partition, session, group, or FIFO key | Any deployment that assumes LiteBus preserves publish order onto a broker: with `DispatcherConcurrency` above `1`, broker arrival order already differs from lease order |

## Batch Order Is Claim Order, Not Completion Order

One pass leases up to `ProcessorOptions.BatchSize` due rows (default `20`) and dispatches them through at most `ProcessorOptions.DispatcherConcurrency` parallel workers (default `1`). When the downstream consumer is sensitive to arrival order, keep the default:

```csharp
outbox.UseProcessorOptions(new OutboxProcessorOptions
{
    BatchSize = 100,
    DispatcherConcurrency = 1,
    LeaseDuration = TimeSpan.FromMinutes(2)
});
```

That configuration serializes dispatch starts within one pass. Completion order still varies with retries, reclaimed leases, and additional processor instances.

When strict per-key order matters across workers, partition the work before LiteBus (one tenant per processor via `ProcessorOptions.TenantId`) or serialize on the saga correlation for state updates. Make the consumer idempotent, because at-least-once redelivery still duplicates.

## Tenancy Partitions Leases, Not Order

Setting `ProcessorOptions.TenantId` restricts one processor instance to one tenant partition. Use it to isolate noisy tenants or to pin a tenant to a serial (`DispatcherConcurrency = 1`) processor. It does not order tenants relative to each other. Rows without a matching tenant are invisible to that instance, so every tenant still needs a processor that leases it.

## Saga Serialization Is Per Correlation and Per Host

The `SagaProcessorHook` gate serializes only dispatches that share tenant, saga definition, and `CorrelationId`, and only within one host process.

Across hosts, correctness comes from `ISagaStore` version checks. Concurrent saves throw `SagaConcurrencyException`. Completion reloads and retries.

Handlers keyed by `SagaCorrelation` therefore see serial state transitions per instance on one host. Everywhere else, they see optimistic-concurrency failures instead of silent overwrites. Different correlations never block each other. See [Saga](saga.md) for registration and the state model.

## Transports Preserve Nothing by Default

LiteBus transport dispatch maps an envelope to `Destination` plus `Route` (tenant strategy, then `ResolveRoute`, then the outbox topic, then the contract name) and publishes. No dispatcher option selects a Kafka partition, an SQS FIFO group, or a Service Bus session. Ingress concurrency (`TransportConsumerOptions.MaxInFlightMessages`, default `32`, plus `MaxConcurrentCalls` and `ReceiveBatchSize` where the broker supports them) redelivers into `IInbox.AcceptAsync` concurrently.

Applications that need broker-ordered intake must configure broker-side ordering (partition keys, FIFO queues, sessions) in their own topology and keep consumer concurrency at 1 per ordered unit. LiteBus records whatever arrives as unordered `Pending` rows.

## Next

- Contracts and registration: [Inbox](inbox.md), [Outbox](outbox.md)
- State model behind retries: [Envelope Lifecycle](../catalog/durable-core/envelope-lifecycle.md)
- Saga state and correlation: [Saga](saga.md)
