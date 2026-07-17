# Command-Inbox Patterns

**ID:** `durable-core.command-inbox-patterns`
**Maturity:** GA

## Summary

Store commands for deferred execution through the inbox instead of synchronous `SendAsync`, replacing the removed v5 command-inbox API with explicit inbox modules and stable contracts.

## What It Does

Commands (`ICommand` without result) are accepted into inbox storage and later dispatched through `IInboxDispatcher`. In-process dispatch deserializes and calls `ICommandMediator.SendAsync`. Transport dispatch publishes leased envelopes to a broker for remote execution. v5 `[StoreInInbox]`, `ICommandInbox`, and hosted command-inbox processor packages are removed; applications register `AddInboxModule`, storage, dispatch, and `EnableInboxProcessor`.

## Public Surface

### Consumer Contracts

- **`IInbox`**: Accept commands into durable storage; returns `InboxReceipt`, not handler results.
- **`InboxAcceptItem<TCommand>`**: Command payload plus accept metadata (`InboxAcceptMetadata.Immediate with { ... }`).
- **`IInboxDispatcher`**: Executes leased inbox rows; in-process variant calls `ICommandMediator`, transport variant publishes to broker.
- **`ICommand` (no result)**: Only command shape eligible for inbox storage; `ICommand<TResult>` blocked at compile time (LB1004).

### Invocation

- **Defer execution**: `IInbox.AcceptAsync<TCommand>(InboxAcceptItem<TCommand>.From(command), ...)` instead of `ICommandMediator.SendAsync`.
- **Factory**: `InboxAcceptItem<TCommand>.From(command)` for body-only accept with default metadata.
- **Process later**: `PipelinedInboxProcessor` (via `EnableInboxProcessor`) leases pending rows and calls registered `IInboxDispatcher`.
- **Remote execution**: Transport dispatcher publishes leased envelope; remote service ingress accepts and in-process dispatch executes locally.

v6 replaces v5 implicit attribute-driven storage with explicit accept calls.

### Registration

- **`AddInboxModule()`** with **`Use*Storage()`**, **`UseInProcessDispatch()`** or transport dispatch, and **`EnableInboxProcessor()`**.
- **`Contracts.Register<TCommand>(name, version)`** for each stored command type.
- Command handler registered in message registry (same as synchronous send path).

### Configuration

- Accept metadata: identity, idempotency, visibility, trace, tenant on **`InboxAcceptItem`**.
- Processor options: batch, lease, retry on inbox module builder.
- Ingress options when commands arrive from external brokers (inbox-ingress capability).

### Extension Points

- **`IInboxDispatcher`**: Swap in-process mediator dispatch for AMQP, Kafka, or other transport dispatch packages.
- **Ingress adapters**: Map broker deliveries to `InboxAcceptItem` for dual-hop command routing.
- **`LiteBus.Analyzers` LB1004**: Compile-time guard against `ICommand<TResult>` in inbox accept APIs.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions` | Core accept + processor |
| `LiteBus.Inbox.Dispatch.InProcess` | Command mediator dispatch |
| `LiteBus.Inbox.Dispatch.*` | Remote command publication |
| `LiteBus.Inbox.Ingress.*` | External command intake |
| `LiteBus.Analyzers` | LB1004 blocks `ICommand<TResult>` in inbox |

## Requires

- Command registered as durable contract
- Command handler registered in message registry
- `ICommand` without result type for inbox storage

## Invariants

- Commands with results cannot be inbox-stored (compile-time LB1004)
- Acceptance returns `InboxReceipt`, not handler result
- Idempotency keys replace v5 `IIdempotentCommand` marker
- Exactly one inbox dispatcher per module

## Non-Goals

- Transparent inbox storage via attributes on handlers
- Storing query or event types as inbox commands without explicit accept
- Request-response over inbox (use synchronous command or separate read model)

## Observability

Command-inbox patterns use the same inbox processor and ingress instrumentation as general inbox acceptance. Dispatch tracing depends on the registered dispatcher package.

### `litebus.inbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: Periodic observation after accepted commands commit to store
- **Tags/dimensions**: `litebus.inbox.status` (`LiteBusInboxTelemetry.QueueStatusAttributeName`)
- **How to enable**: `AddLiteBusInboxMetrics()` from `LiteBus.Inbox.Extensions.OpenTelemetry`
- **Operational note**: Pending command depth rising while succeeded rate is flat indicates processor or dispatch bottleneck

### `litebus.inbox.processor.succeeded` / `failed` / `dead_lettered`

- **Kind**: Counter (pass-aggregated)
- **When emitted**: After each inbox processor pass completes command dispatch
- **Tags/dimensions**: None; pass activity may carry leased/succeeded/failed/dead_lettered counts
- **How to enable**: `AddLiteBusInboxMetrics()`
- **Operational note**: Core signal for deferred command execution health; failed commands retry per lease/retry policy

### `litebus.inbox.processor.state`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.ProcessorStateInstrumentName`)
- **When emitted**: When processor control is registered
- **Tags/dimensions**: None (`0` Running, `1` Paused, `2` Draining)
- **How to enable**: `AddLiteBusInboxMetrics()`
- **Operational note**: Pause or drain during deploy without losing accepted commands

### Dispatch Path Tracing

- **Kind**: Activity (`LiteBus.Inbox` activity source for processor; transport dispatch adds broker-specific sources)
- **When emitted**: Per dispatch attempt when OpenTelemetry tracing is registered for inbox and transport packages
- **Tags/dimensions**: Trace metadata copied from accept metadata; in-process dispatch sets `IsInboxExecution` on execution context
- **How to enable**: `AddLiteBusInboxInstrumentation()` plus transport or in-process dispatch OpenTelemetry package as applicable
- **Operational note**: Dual-hop AMQP patterns produce separate ingress and dispatch spans; correlate via trace metadata on accept

## Deep Docs

- [Inbox](../../reliable-messaging/inbox.md)
- [Migration Guide v5](../../migration/v5.md)
- [Command module](../../concepts/commands.md)

## Test Coverage

### Covered Use Cases

#### `InboxTests.ProcessPendingAsync_ShouldExecuteCommandThroughMediatorAndMarkCompleted`

- **Use case**: Accept then in-process command dispatch
- **Test kind**: Unit
- **Description**: Full accept, processor, and handler chain in test host
- **Behavior**: Accept command, run processor pass
- **Expected outcome**: Row completed; handler invoked
- **Remarks**: Core v6 pattern

#### `InboxTests.ProcessPendingAsync_ShouldSupportClosedGenericCommands`

- **Use case**: Closed generic command inbox path
- **Test kind**: Unit
- **Description**: Closed generic command type registered and handled
- **Behavior**: Accept and process closed generic command
- **Expected outcome**: Handler runs successfully
- **Remarks**:

#### `InProcessInboxDispatcherTests.DispatchAsync_ShouldExecuteCommandThroughMediator`

- **Use case**: In-process dispatcher executes command
- **Test kind**: Unit
- **Description**: Direct `IInboxDispatcher` invocation
- **Behavior**: `IInboxDispatcher` deserializes and calls mediator
- **Expected outcome**: Command handled
- **Remarks**:

#### `InProcessInboxDispatcherTests.DispatchAsync_WhenMessageIsNotACommand_ShouldThrowInvalidOperationException`

- **Use case**: Non-command message rejected at dispatch
- **Test kind**: Unit
- **Description**: Event type stored in inbox row (invalid for in-process command dispatch)
- **Behavior**: Dispatch non-command envelope
- **Expected outcome**: InvalidOperationException
- **Remarks**:

#### `InProcessInboxDispatcherTests.DispatchAsync_ShouldSetIsInboxExecutionAndCopyTraceMetadata`

- **Use case**: Inbox execution context flag set
- **Test kind**: Unit
- **Description**: Dispatch context propagation
- **Behavior**: In-process dispatch with trace metadata on envelope
- **Expected outcome**: `IsInboxExecution` true
- **Remarks**:

#### `CommandWithResultScheduledToInboxAnalyzerTests.ExplicitGenericAcceptAsyncWithCommandResult_ProducesDiagnostic`

- **Use case**: LB1004 blocks command with result in inbox
- **Test kind**: Analyzer
- **Description**: Compile-time analysis of `ICommand<TResult>` accept
- **Behavior**: Explicit generic accept with command result type
- **Expected outcome**: Compile-time LB1004
- **Remarks**:

#### `AmqpInboxIngressEndToEndTests.PublishThroughRabbitMq_ShouldAcceptProcessAndDispatchCommand`

- **Use case**: AMQP ingress accept process dispatch
- **Test kind**: Integration
- **Description**: Broker to ingress to processor chain
- **Behavior**: Publish command to RabbitMQ, ingress accepts, processor dispatches
- **Expected outcome**: Command executed
- **Remarks**: Dual-hop entry

#### `PostgreSqlInboxEndToEndTests.ProcessPendingAsync_ShouldExecuteScheduledCommandThroughPostgreSqlStore`

- **Use case**: PostgreSQL command inbox E2E
- **Test kind**: Integration
- **Description**: Persistent store with in-process dispatch
- **Behavior**: Accept and process on PostgreSQL-backed inbox
- **Expected outcome**: Completed row
- **Remarks**:

#### `InboxCompositeModuleTests.NestedConfiguration_ShouldAcceptAndProcessCommand`

- **Use case**: Nested module accept and process
- **Test kind**: Unit
- **Description**: v6 composite module registration
- **Behavior**: Nested inbox module configuration in test host
- **Expected outcome**: End-to-end in test host
- **Remarks**: Replaces v5 worker

#### `TransportInboxDispatcherTests.DispatchAsync_ShouldPublishEnvelopeThroughTransport`

- **Use case**: Transport inbox dispatch publishes envelope
- **Test kind**: Unit
- **Description**: Remote execution path via transport
- **Behavior**: Transport dispatcher publishes leased envelope
- **Expected outcome**: Envelope on transport
- **Remarks**: Dual-hop publish

#### `AmqpInboxDispatcherIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToAmqpQueue`

- **Use case**: AMQP inbox dispatch integration
- **Test kind**: Integration
- **Description**: Leased row published to AMQP queue
- **Behavior**: Processor pass with AMQP transport dispatcher
- **Expected outcome**: Message on broker queue
- **Remarks**:

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Same-DB transactional accept of follow-up command inside handler | Yes | Rare pattern; only generic transactional tests | Integration | Low |
| v5 `[StoreInInbox]` migration smoke test | N/A (removed) | Migration doc only |: |: |
| Query type accepted to inbox | No (by design) | No explicit rejection test beyond command result | Unit | Low |

### Out-of-Scope Use Cases

- Transparent attribute-driven inbox storage (v5 removed)
- Request-response over inbox
- Inbox storage of `ICommand<TResult>`
