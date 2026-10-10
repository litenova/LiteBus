# Reliable Messaging Semantics

**ID:** `durable-core.reliable-messaging-semantics`
**Maturity:** GA

## Summary

LiteBus durable paths provide at-least-once acceptance, execution, and publication with store-backed recovery; they do not provide exactly-once side effects.

## What It Does

Messages persist before execution (inbox) or before terminal publish state (outbox). Processors lease rows, dispatch, and record outcomes. Crash, lease expiry, broker redelivery, or persist interruption can cause duplicate dispatch or publication. Applications deduplicate through idempotency keys, handler design, and consumer logic.

## Public Surface

### Consumer Contracts

- **`IInbox` / `IOutbox`**: Writer contracts that durably persist before execution or publication (acceptance and enqueue capabilities).
- **`IInboxProcessor` / `IOutboxProcessor`**: Processor contracts that lease and dispatch persisted rows (inbox-processor and outbox-processor capabilities).
- **`InboxReceipt` / `OutboxReceipt`**: Confirm storage only; not handler results or publish acknowledgments.
- **`ProcessorOptions` / axis-specific processor options**: Lease, retry, concurrency, and shutdown behavior shared across inbox and outbox processors.

### Invocation

Semantics emerge from the separation of writer and processor calls rather than a single API:

- **Accept vs execute**: `IInbox.AcceptAsync` stores the command; `PipelinedInboxProcessor` executes it later through `IInboxDispatcher`.
- **Enqueue vs publish**: `IOutbox.EnqueueAsync` stores the event; `PipelinedOutboxProcessor` publishes it later through `IOutboxDispatcher`.
- **Ingress intake**: Broker consumers call accept after mapping; ack timing is broker-specific and may lag store commit.

Duplicate delivery is expected at every boundary after the initial durable write.

### Registration

- Writers and processors register through **`AddMessaging`** then **`AddInbox`** / **`AddOutbox`** with storage and exactly one dispatcher each.
- Ingress registers through inbox module **`Use*Ingress`** adapters when external intake is required.

### Configuration

- **`HonorShutdownTokenOnPersist`** on `InboxProcessorOptions` / `OutboxProcessorOptions`: When enabled, terminal persist honors host shutdown token; when disabled, persist uses `CancellationToken.None` to reduce duplicate dispatch/publication after graceful stop.
- **Ingress requeue options**: Control whether transient accept failures requeue broker deliveries or discard poison messages without store writes.
- **Lease and retry options**: Batch size, lease duration, heartbeat, and max attempts define recovery windows and at-least-once retry behavior.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox`, `LiteBus.Outbox` | Processor orchestration |
| `LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*` | Lease and conditional persist |
| `LiteBus.Inbox.Ingress.*` | Broker-to-store intake |

## Requires

- Registered storage and exactly one dispatcher per enabled processor
- Idempotent handlers or deduplication strategy for side effects

## Invariants

- Accept/enqueue commits when the store write commits
- Execution/publication is at-least-once under lease
- Terminal persist uses conditional update on `lease_owner` where stores support it
- Processors are not atomic with external side effects by design

## Non-Goals

- Exactly-once end-to-end delivery
- Distributed transactions spanning broker and database
- Built-in handler-level idempotency record store (planned on [Roadmap](../../roadmap/README.md))

## Observability

Reliability semantics surface through processor state, pass counters, lease-loss signals, and ingress ack-failure telemetry. No single instrument represents end-to-end exactly-once delivery (by design).

### `litebus.inbox.processor.state`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.ProcessorStateInstrumentName`)
- **When emitted**: Periodic observation when `IInboxProcessorControl` is registered (`0` Running, `1` Paused, `2` Draining)
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusInboxMetrics()` from `LiteBus.Inbox.Extensions.OpenTelemetry`
- **Operational note**: Draining during deploy confirms in-flight work completion policy; paused state blocks execution without losing accepted rows

### `litebus.outbox.processor.state`

- **Kind**: Observable gauge (`LiteBusOutboxTelemetry.ProcessorStateInstrumentName`)
- **When emitted**: Periodic observation when `IOutboxProcessorControl` is registered
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusOutboxMetrics()` from `LiteBus.Outbox.Extensions.OpenTelemetry`
- **Operational note**: Same lifecycle semantics as inbox; pair with pending queue depth during publication backlog incidents

### `litebus.inbox.processor.succeeded` / `litebus.inbox.processor.failed` / `litebus.inbox.processor.dead_lettered`

- **Kind**: Counter (pass-aggregated)
- **When emitted**: After each inbox processor pass completes terminal state updates
- **Tags/dimensions**: None on counters; pass activity may carry `litebus.inbox.leased_count`, `succeeded_count`, `failed_count`, `dead_lettered_count` tags
- **How to enable**: `AddLiteBusInboxMetrics()`
- **Operational note**: Failed plus dead-lettered growth with flat succeeded rate indicates handler or dispatch failures under at-least-once retry

### `litebus.outbox.processor.published` / `litebus.outbox.processor.failed` / `litebus.outbox.processor.dead_lettered`

- **Kind**: Counter (pass-aggregated)
- **When emitted**: After each outbox processor pass completes terminal state updates
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusOutboxMetrics()`
- **Operational note**: Published count can exceed logical sends when persist is skipped after broker ack (at-least-once publication window)

### `litebus.inbox.processor.persist_skipped` / `litebus.outbox.processor.persist_skipped`

- **Kind**: Counter (`LiteBusInboxTelemetry.ProcessorPersistSkippedInstrumentName` / `LiteBusOutboxTelemetry.ProcessorPersistSkippedInstrumentName`)
- **When emitted**: Terminal persist skips because active lease no longer matches (also increments `persist_rejected`)
- **Tags/dimensions**: None
- **How to enable**: Respective axis `AddLiteBus*Metrics()`
- **Operational note**: Non-zero rate under parallel workers is expected; combined with republish/re-dispatch confirms at-least-once semantics

### `litebus.inbox.processor.persist_failed` / `litebus.outbox.processor.persist_failed`

- **Kind**: Counter (`LiteBusInboxTelemetry.ProcessorPersistFailedInstrumentName` / `LiteBusOutboxTelemetry.ProcessorPersistFailedInstrumentName`)
- **When emitted**: Terminal persist throws after successful dispatch or broker publish
- **Tags/dimensions**: None
- **How to enable**: Respective axis `AddLiteBus*Metrics()`
- **Operational note**: Logged exception; row stays leased or pending and may retry, producing duplicate side effects without idempotency

### `ingress.ack_failed_after_accept`

- **Kind**: Counter (`LiteBusInboxIngressTelemetry.AckFailedAfterAcceptInstrumentName`)
- **When emitted**: Broker `AcceptAsync` throws after `IInbox` accept succeeded
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusInboxMetrics()` (same meter `LiteBus.Inbox`)
- **Operational note**: Consumer requeues after recording; idempotent accept absorbs redelivery. Pair with log EventId 3004 (`AckFailedAfterAccept`)

## Deep Docs

- [Reliable messaging semantics](../../reliable-messaging/semantics.md)
- [Reliable messaging](../../reliable-messaging/README.md)
- [Production runbook](../../operations/runbook.md)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Inbox.UnitTests`
- `LiteBus.Outbox.UnitTests`
- `LiteBus.Durable.IntegrationTests`
- `LiteBus.Storage.IntegrationTests`

### Covered Use Cases

#### `PostgreSqlReliableMessagingEndToEndTests.OutboxToInbox_ShouldPublishProcessAndDispatchCommand`

- **Use case**: Outbox-to-inbox cross-service at-least-once
- **Test kind**: Integration
- **Description**: Full publish, accept, dispatch chain across services
- **Behavior**: Outbox processor publishes; inbox ingress accepts; inbox processor dispatches command
- **Expected outcome**: Command executed once per logical send
- **Remarks**: PostgreSQL + RabbitMQ

#### `PostgreSqlReliableMessagingEndToEndTests.DuplicateBrokerDelivery_ShouldExecuteHandlerOnce`

- **Use case**: Duplicate broker delivery deduped at accept
- **Test kind**: Integration
- **Description**: Redelivered broker message after successful accept
- **Behavior**: Second broker delivery with same idempotency semantics
- **Expected outcome**: Handler invoked once
- **Remarks**: Idempotency at ingress

#### `InMemoryInboxAtLeastOnceIntegrationTests.ProcessPendingAsync_WhenPersistSkippedAfterDispatch_ShouldRedispatchOnRetry`

- **Use case**: Inbox redispatch after persist skip
- **Test kind**: Integration
- **Description**: Simulated crash window after handler success before terminal persist
- **Behavior**: Processor pass with persist skip after successful dispatch
- **Expected outcome**: Second handler invocation on retry
- **Remarks**: At-least-once execution; `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`)

#### `InMemoryOutboxAtLeastOnceIntegrationTests.ProcessPendingAsync_WhenPersistSkippedAfterPublish_ShouldRepublishOnRetry`

- **Use case**: Outbox republish after persist skip
- **Test kind**: Integration
- **Description**: Simulated crash window after broker publish before terminal persist
- **Behavior**: Processor pass with persist skip after successful dispatch
- **Expected outcome**: Second publish on retry
- **Remarks**: At-least-once publication

#### `InboxTests.ProcessPendingAsync_WhenMarkCompletedFailsAfterSuccessfulDispatch_ShouldNotMarkFailed`

- **Use case**: Successful dispatch not marked failed when persist fails
- **Test kind**: Unit
- **Description**: Terminal persist failure after handler success
- **Behavior**: `MarkCompleted` throws after successful dispatch
- **Expected outcome**: Row not downgraded to failed
- **Remarks**: Inbox semantics

#### `PostgreSqlInboxProcessorLeaseStressTests.ProcessPendingAsync_parallel_workers_should_produce_single_terminal_state_per_message`

- **Use case**: Concurrent workers single terminal state
- **Test kind**: Integration
- **Description**: Multiple processor instances leasing same pending rows
- **Behavior**: Parallel `ProcessPendingAsync` workers
- **Expected outcome**: Exactly one terminal outcome per id
- **Remarks**: Lease + conditional persist

#### `OutboxStoreContractTests.PersistAsync_WhenCompetingLeaseOwnersPersistConcurrently_ShouldApplyExactlyOnce`

- **Use case**: Competing lease owners persist once
- **Test kind**: Contract
- **Description**: Concurrent terminal persist from two lease owners
- **Behavior**: Conditional update on `lease_owner`
- **Expected outcome**: One winner
- **Remarks**:

#### `OutboxProcessorCorrectnessTests.PipelinedProcessor_when_honor_shutdown_enabled_should_pass_dispatch_token_to_persist`

- **Use case**: Honor shutdown token on persist
- **Test kind**: Unit
- **Description**: Processor with `HonorShutdownTokenOnPersist` enabled
- **Behavior**: Terminal persist receives shutdown cancellation token
- **Expected outcome**: Shutdown token forwarded to persist
- **Remarks**:

#### `InMemoryIngressRequeueBehaviorIntegrationTests.RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept`

- **Use case**: Ingress requeue on transient store failure
- **Test kind**: Integration
- **Description**: Flaky store then recovery with requeue enabled
- **Behavior**: Transient accept failure at ingress
- **Expected outcome**: Message eventually accepted
- **Remarks**:

#### `InMemoryIngressRequeueBehaviorIntegrationTests.RequeueDisabled_WithPoisonMessage_ShouldDiscardWithoutStoreWrite`

- **Use case**: Ingress discard poison without store write
- **Test kind**: Integration
- **Description**: Invalid contract with requeue disabled
- **Behavior**: Poison delivery at ingress boundary
- **Expected outcome**: No row created
- **Remarks**:

#### `AmqpIngressRequeueBehaviorIntegrationTests.RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept`

- **Use case**: AMQP ingress requeue on transient failure
- **Test kind**: Integration
- **Description**: Real broker plus flaky accept path
- **Behavior**: Transient store failure during AMQP ingress accept
- **Expected outcome**: Eventually accepted
- **Remarks**:

#### `PostgreSqlProcessCrashIntegrationTests.AcceptedCommand_WhenWorkerProcessIsKilled_ShouldRecoverAfterLeaseExpiry`

- **Use case**: Worker process terminates during an active handler
- **Test kind**: Integration
- **Description**: PostgreSQL inbox plus a child test host terminated with a process-tree kill
- **Behavior**: The accepted row remains leased after process termination
- **Expected outcome**: A replacement processor reclaims the expired lease and completes attempt two
- **Remarks**: Exercises an actual process boundary rather than simulated persistence failure

#### `GenericHostDurableShutdownTests.StopAsync_WhenInboxDispatchIsActive_ShouldWaitForCompletionAndPersistTerminalState`

- **Use case**: Graceful host shutdown during active inbox dispatch
- **Test kind**: Host integration
- **Description**: Microsoft Generic Host stops while an in-process handler is active
- **Behavior**: The dispatch token is canceled while host stop waits for the handler
- **Expected outcome**: The handler finishes and the terminal state is persisted before stop completes
- **Remarks**: Uses the default `HonorShutdownTokenOnPersist = false` policy

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Plain SendAsync vs AcceptAsync crash comparison | Yes | Documented only; no side-by-side test | Integration | Low |
| Distributed transaction broker + database | No (by design) | N/A |: |: |

### Out-of-Scope Use Cases

- Exactly-once end-to-end delivery
- Built-in handler-level idempotency record store
- Cross-broker transactional commit
