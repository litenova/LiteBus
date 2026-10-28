# Reliable Messaging Semantics

**ID:** `durable-core.reliable-messaging-semantics`
**Maturity:** GA

## Summary

LiteBus durable paths provide at-least-once acceptance, execution, and publication with store-backed recovery. They do not provide exactly-once side effects.

## What It Does

Messages persist before execution (inbox) or before terminal publish state (outbox). Processors lease rows, dispatch, and record outcomes. Crash, lease expiry, broker redelivery, or persist interruption can cause duplicate dispatch or publication. Applications deduplicate through idempotency keys, handler design, and consumer logic.

## Public Surface

### Consumer Contracts

- **`IInbox` / `IOutbox`**: These are the writer contracts that durably persist before execution or publication (acceptance and enqueue capabilities).
- **`IInboxProcessor` / `IOutboxProcessor`**: These are the processor contracts that lease and dispatch persisted rows (inbox-processor and outbox-processor capabilities).
- **`InboxReceipt` / `OutboxReceipt`**: These receipts confirm storage only, not handler results or publish acknowledgments.
- **`ProcessorOptions` / axis-specific processor options**: These define the lease, retry, concurrency, and shutdown behavior shared across inbox and outbox processors.

### Invocation

Semantics emerge from the separation of writer and processor calls rather than a single API:

- **Accept vs execute**: `IInbox.AcceptAsync` stores the command, and `PipelinedInboxProcessor` executes it later through `IInboxDispatcher`.
- **Enqueue vs publish**: `IOutbox.EnqueueAsync` stores the event, and `PipelinedOutboxProcessor` publishes it later through `IOutboxDispatcher`.
- **Ingress intake**: Broker consumers call accept after mapping. Ack timing is broker-specific and may lag the store commit.

Duplicate delivery is expected at every boundary after the initial durable write.

### Registration

- Writers and processors register through **`AddMessaging`** then **`AddInbox`** / **`AddOutbox`** with storage and exactly one dispatcher each.
- Ingress registers through inbox module **`Use*Ingress`** adapters when external intake is required.

### Configuration

- **`HonorShutdownTokenOnPersist`** on `InboxProcessorOptions` / `OutboxProcessorOptions` controls shutdown behavior. When enabled, terminal persist honors the host shutdown token. When disabled, persist uses `CancellationToken.None` to reduce duplicate dispatch or publication after graceful stop.
- **Ingress requeue options** control whether transient accept failures requeue broker deliveries or discard poison messages without store writes.
- **Lease and retry options** define recovery windows and at-least-once retry behavior: batch size, lease duration, heartbeat, and max attempts.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox`, `LiteBus.Outbox` | Processor orchestration |
| `LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*` | Lease and conditional persist |
| `LiteBus.Inbox.Ingress.*` | Broker-to-store intake |

## Requires

- Register storage and exactly one dispatcher per enabled processor.
- Write idempotent handlers or provide a deduplication strategy for side effects.

## Invariants

- Accept and enqueue commit when the store write commits.
- Execution and publication are at-least-once under lease.
- Terminal persist uses conditional update on `lease_owner` where stores support it.
- Processors are not atomic with external side effects by design.

## Non-Goals

- These paths do not provide exactly-once end-to-end delivery.
- These paths do not provide distributed transactions spanning broker and database.
- Mediation idempotency is a separate capability built on `IdempotencyDeclaration` with `IIdempotencyStore` (see [Idempotency](../../concepts/idempotency.md)), not part of durable delivery semantics.

## Observability

Reliability semantics surface through processor state, pass counters, lease-loss signals, and ingress ack-failure telemetry. No single instrument represents end-to-end exactly-once delivery (by design).

### `litebus.inbox.processor.state`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.ProcessorStateInstrumentName`)
- **When emitted**: Periodic observation when `IInboxProcessorControl` is registered (`0` Running, `1` Paused, `2` Draining)
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusInboxMetrics()` from `LiteBus.Inbox.Extensions.OpenTelemetry`
- **Operational note**: Draining during deploy confirms the in-flight work completion policy. Paused state blocks execution without losing accepted rows.

### `litebus.outbox.processor.state`

- **Kind**: Observable gauge (`LiteBusOutboxTelemetry.ProcessorStateInstrumentName`)
- **When emitted**: Periodic observation when `IOutboxProcessorControl` is registered
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusOutboxMetrics()` from `LiteBus.Outbox.Extensions.OpenTelemetry`
- **Operational note**: It has the same lifecycle semantics as inbox. Pair it with pending queue depth during publication backlog incidents.

### `litebus.inbox.processor.succeeded` / `litebus.inbox.processor.failed` / `litebus.inbox.processor.dead_lettered`

- **Kind**: Counter (pass-aggregated)
- **When emitted**: After each inbox processor pass completes terminal state updates
- **Tags/dimensions**: None on counters. Pass activity may carry `litebus.inbox.leased_count`, `succeeded_count`, `failed_count`, `dead_lettered_count` tags.
- **How to enable**: `AddLiteBusInboxMetrics()`
- **Operational note**: Growth in failed plus dead-lettered counts with a flat succeeded rate indicates handler or dispatch failures under at-least-once retry.

### `litebus.outbox.processor.published` / `litebus.outbox.processor.failed` / `litebus.outbox.processor.dead_lettered`

- **Kind**: Counter (pass-aggregated)
- **When emitted**: After each outbox processor pass completes terminal state updates
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusOutboxMetrics()`
- **Operational note**: Published count can exceed logical sends when persist is skipped after broker ack (at-least-once publication window).

### `litebus.inbox.processor.persist_skipped` / `litebus.outbox.processor.persist_skipped`

- **Kind**: Counter (`LiteBusInboxTelemetry.ProcessorPersistSkippedInstrumentName` / `LiteBusOutboxTelemetry.ProcessorPersistSkippedInstrumentName`)
- **When emitted**: The terminal persist skips because the active lease no longer matches (also increments `persist_rejected`)
- **Tags/dimensions**: None
- **How to enable**: Respective axis `AddLiteBus*Metrics()`
- **Operational note**: A non-zero rate under parallel workers is expected. Combined with republish or redispatch, it confirms at-least-once semantics.

### `litebus.inbox.processor.persist_failed` / `litebus.outbox.processor.persist_failed`

- **Kind**: Counter (`LiteBusInboxTelemetry.ProcessorPersistFailedInstrumentName` / `LiteBusOutboxTelemetry.ProcessorPersistFailedInstrumentName`)
- **When emitted**: The terminal persist throws after successful dispatch or broker publish
- **Tags/dimensions**: None
- **How to enable**: Respective axis `AddLiteBus*Metrics()`
- **Operational note**: The exception is logged. The row stays leased or pending and may retry, producing duplicate side effects without idempotency.

### `ingress.ack_failed_after_accept`

- **Kind**: Counter (`LiteBusInboxIngressTelemetry.AckFailedAfterAcceptInstrumentName`)
- **When emitted**: Broker `AcceptAsync` throws after `IInbox` accept succeeded
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusInboxMetrics()` (same meter `LiteBus.Inbox`)
- **Operational note**: The consumer requeues after the failure is recorded, and idempotent accept absorbs the redelivery. Pair with log EventId 3004 (`AckFailedAfterAccept`).

## References

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

- **Use case**: An outbox-to-inbox cross-service flow stays at-least-once
- **Test kind**: Integration
- **Description**: A full publish, accept, and dispatch chain across services
- **Behavior**: The outbox processor publishes, inbox ingress accepts, and the inbox processor dispatches the command.
- **Expected outcome**: The command executes once per logical send
- **Remarks**: PostgreSQL with RabbitMQ

#### `PostgreSqlReliableMessagingEndToEndTests.DuplicateBrokerDelivery_ShouldExecuteHandlerOnce`

- **Use case**: A duplicate broker delivery is deduped at accept
- **Test kind**: Integration
- **Description**: A redelivered broker message after successful accept
- **Behavior**: A second broker delivery with the same idempotency semantics
- **Expected outcome**: The handler is invoked once
- **Remarks**: Idempotency at ingress

#### `InMemoryInboxAtLeastOnceIntegrationTests.ProcessPendingAsync_WhenPersistSkippedAfterDispatch_ShouldRedispatchOnRetry`

- **Use case**: The inbox redispatches after a persist skip
- **Test kind**: Integration
- **Description**: A simulated crash window after handler success but before terminal persist
- **Behavior**: A processor pass with a persist skip after successful dispatch
- **Expected outcome**: The retry invokes the handler a second time
- **Remarks**: At-least-once execution; `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`)

#### `InMemoryOutboxAtLeastOnceIntegrationTests.ProcessPendingAsync_WhenPersistSkippedAfterPublish_ShouldRepublishOnRetry`

- **Use case**: The outbox republishes after a persist skip
- **Test kind**: Integration
- **Description**: A simulated crash window after broker publish but before terminal persist
- **Behavior**: A processor pass with a persist skip after successful dispatch
- **Expected outcome**: The retry publishes a second time
- **Remarks**: At-least-once publication

#### `InboxTests.ProcessPendingAsync_WhenMarkCompletedFailsAfterSuccessfulDispatch_ShouldNotMarkFailed`

- **Use case**: A successful dispatch is not marked failed when persist fails
- **Test kind**: Unit
- **Description**: A terminal persist failure after handler success
- **Behavior**: `MarkCompleted` throws after successful dispatch
- **Expected outcome**: The row is not downgraded to failed
- **Remarks**: Inbox semantics

#### `PostgreSqlInboxProcessorLeaseStressTests.ProcessPendingAsync_parallel_workers_should_produce_single_terminal_state_per_message`

- **Use case**: Concurrent workers produce a single terminal state
- **Test kind**: Integration
- **Description**: Multiple processor instances leasing the same pending rows
- **Behavior**: Parallel `ProcessPendingAsync` workers
- **Expected outcome**: There is exactly one terminal outcome per id
- **Remarks**: Lease with conditional persist

#### `OutboxStoreContractTests.PersistAsync_WhenCompetingLeaseOwnersPersistConcurrently_ShouldApplyExactlyOnce`

- **Use case**: Of competing lease owners, only one persists
- **Test kind**: Contract
- **Description**: A concurrent terminal persist from two lease owners
- **Behavior**: Conditional update on `lease_owner`
- **Expected outcome**: Exactly one persist succeeds
- **Remarks**:

#### `OutboxProcessorCorrectnessTests.PipelinedProcessor_when_honor_shutdown_enabled_should_pass_dispatch_token_to_persist`

- **Use case**: The processor honors the shutdown token on persist
- **Test kind**: Unit
- **Description**: A processor with `HonorShutdownTokenOnPersist` enabled
- **Behavior**: The terminal persist receives the shutdown cancellation token
- **Expected outcome**: The shutdown token is forwarded to persist
- **Remarks**:

#### `InMemoryIngressRequeueBehaviorIntegrationTests.RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept`

- **Use case**: Ingress requeues on transient store failure
- **Test kind**: Integration
- **Description**: A failing store that recovers, with requeue enabled
- **Behavior**: A transient accept failure at ingress
- **Expected outcome**: The message is eventually accepted
- **Remarks**:

#### `InMemoryIngressRequeueBehaviorIntegrationTests.RequeueDisabled_WithPoisonMessage_ShouldDiscardWithoutStoreWrite`

- **Use case**: Ingress discards a poison message without a store write
- **Test kind**: Integration
- **Description**: An invalid contract with requeue disabled
- **Behavior**: A poison delivery at the ingress boundary
- **Expected outcome**: No row is created
- **Remarks**:

#### `AmqpIngressRequeueBehaviorIntegrationTests.RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept`

- **Use case**: AMQP ingress requeues on transient failure
- **Test kind**: Integration
- **Description**: A real broker with a transiently failing accept path
- **Behavior**: A transient store failure during AMQP ingress accept
- **Expected outcome**: The message is eventually accepted
- **Remarks**:

#### `PostgreSqlProcessCrashIntegrationTests.AcceptedCommand_WhenWorkerProcessIsKilled_ShouldRecoverAfterLeaseExpiry`

- **Use case**: A worker process terminates during an active handler
- **Test kind**: Integration
- **Description**: PostgreSQL inbox with a child test host terminated by a process-tree kill
- **Behavior**: The accepted row remains leased after process termination
- **Expected outcome**: A replacement processor reclaims the expired lease and completes attempt two
- **Remarks**: Exercises an actual process boundary rather than a simulated persistence failure

#### `GenericHostDurableShutdownTests.StopAsync_WhenInboxDispatchIsActive_ShouldWaitForCompletionAndPersistTerminalState`

- **Use case**: A graceful host shutdown during active inbox dispatch
- **Test kind**: Host integration
- **Description**: Microsoft Generic Host stops while an in-process handler is active
- **Behavior**: The dispatch token is canceled while the host stop waits for the handler
- **Expected outcome**: The handler finishes and the terminal state is persisted before the stop completes
- **Remarks**: Uses the default `HonorShutdownTokenOnPersist = false` policy

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Plain SendAsync compared with AcceptAsync on crash | Yes | Documented only, with no side-by-side test | Integration | Low |
| Distributed transaction across broker and database | No (by design) | Not supported | N/A | N/A |

### Out-of-Scope Use Cases

- Exactly-once end-to-end delivery
- Mediation idempotency record store, which lives on the mediation axis (see [Idempotency](../../concepts/idempotency.md))
- Cross-broker transactional commit
