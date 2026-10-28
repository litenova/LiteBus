# Command-Inbox Patterns

**ID:** `durable-core.command-inbox-patterns`
**Maturity:** GA

## Summary

The command-inbox pattern stores commands in the inbox for deferred execution instead of dispatching them directly through `ICommandMediator.SendAsync`.

## What It Does

The inbox accepts commands (`ICommand` without a result) into storage and dispatches them later through `IInboxDispatcher`. In-process dispatch deserializes each command and calls `ICommandMediator.SendAsync`. Transport dispatch publishes leased envelopes to a broker for remote execution. Applications register `AddInbox`, storage, dispatch, and `EnableInboxProcessor` explicitly.

## Public Surface

### Consumer Contracts

- **`IInbox`**: Accepts commands into durable storage and returns `InboxReceipt`, not handler results.
- **`InboxAcceptItem<TCommand>`**: Carries one command payload with its accept metadata (`InboxAcceptMetadata.Immediate with { ... }`).
- **`IInboxDispatcher`**: Executes leased inbox rows. The in-process variant calls `ICommandMediator`, and the transport variant publishes to a broker.
- **`ICommand` (no result)**: The only command shape eligible for inbox storage. `ICommand<TResult>` is blocked at compile time (LB1004).

### Invocation

- **Defer execution**: Call `IInbox.AcceptAsync<TCommand>(InboxAcceptItem<TCommand>.From(command), ...)` instead of `ICommandMediator.SendAsync`.
- **Factory**: `InboxAcceptItem<TCommand>.From(command)` accepts a body with default metadata.
- **Process later**: `PipelinedInboxProcessor` (through `EnableInboxProcessor`) leases pending rows and calls the registered `IInboxDispatcher`.
- **Remote execution**: The transport dispatcher publishes the leased envelope. Remote service ingress accepts it, and in-process dispatch executes it locally.

Applications call `IInbox.AcceptAsync` explicitly. Attributes do not redirect mediator calls into inbox storage.

### Registration

- **`AddInbox()`** with **`Use*Storage()`**, **`UseInProcessDispatch()`** or transport dispatch, and **`EnableInboxProcessor()`**.
- **`Contracts.Register<TCommand>(name, version)`** for each stored command type.
- The command handler is registered in the message registry, as with the synchronous send path.

### Configuration

- Set accept metadata (identity, idempotency, visibility, trace, tenant) on **`InboxAcceptItem`**.
- Set processor options (batch, lease, retry) on the inbox module builder.
- Configure ingress options when commands arrive from external brokers (inbox-ingress capability).

### Extension Points

- **`IInboxDispatcher`**: Swap in-process mediator dispatch for AMQP, Kafka, or another transport dispatch package.
- **Ingress adapters**: Map broker deliveries to `InboxAcceptItem` for dual-hop command routing.
- **`LiteBus.Analyzers` LB1004**: Provides a compile-time guard against `ICommand<TResult>` in inbox accept APIs.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions` | Core accept and processor roles |
| `LiteBus.Inbox.Dispatch.InProcess` | Command mediator dispatch |
| `LiteBus.Inbox.Dispatch.*` | Remote command publication |
| `LiteBus.Inbox.Ingress.*` | External command intake |
| `LiteBus.Analyzers` | LB1004 blocks `ICommand<TResult>` in inbox accept calls |

## Requires

- The application registers the command as a durable contract
- The application registers the command handler in the message registry
- The stored type is `ICommand` without a result type

## Invariants

- The inbox cannot store commands with results (the compiler reports LB1004)
- Acceptance returns `InboxReceipt`, not the handler result
- Idempotency keys are declared through `InboxAcceptMetadata.Idempotency` or enforced by application logic
- Each module registers exactly one inbox dispatcher

## Non-Goals

- The inbox does not store commands transparently through attributes on handlers
- The inbox does not store query or event types as commands without an explicit accept
- Request-response does not run over the inbox. Use a synchronous command or a separate read model

## Observability

Command-inbox patterns use the same inbox processor and ingress instrumentation as general inbox acceptance. Dispatch tracing depends on the registered dispatcher package.

### `litebus.inbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: A periodic scrape with a 10-second cache, reading counts through `IInboxDiagnosticsStore.GetStatusCountsAsync`
- **Tags/dimensions**: `litebus.inbox.status` (`LiteBusInboxTelemetry.QueueStatusAttributeName`)
- **How to enable**: `AddLiteBusInboxMetrics()` from `LiteBus.Inbox.Extensions.OpenTelemetry`
- **Operational note**: When pending command depth rises while the succeeded rate stays flat, the processor or dispatch is the bottleneck

### `litebus.inbox.processor.succeeded` / `failed` / `dead_lettered`

- **Kind**: Counter (pass-aggregated)
- **When emitted**: After each inbox processor pass completes its command dispatch
- **Tags/dimensions**: None. The pass activity may carry leased, succeeded, failed, and dead_lettered counts
- **How to enable**: `AddLiteBusInboxMetrics()`
- **Operational note**: This is the core signal for deferred command execution health. Failed commands retry under the lease and retry policy

### `litebus.inbox.processor.state`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.ProcessorStateInstrumentName`)
- **When emitted**: When processor control is registered
- **Tags/dimensions**: None (`0` Running, `1` Paused, `2` Draining)
- **How to enable**: `AddLiteBusInboxMetrics()`
- **Operational note**: Pause or drain the processor during a deploy without losing accepted commands

### Dispatch Path Tracing

- **Kind**: Activity (`LiteBus.Inbox` activity source for the processor; transport dispatch adds broker-specific sources)
- **When emitted**: On each dispatch attempt, when OpenTelemetry tracing is registered for the inbox and transport packages
- **Tags/dimensions**: Trace metadata is copied from the accept metadata. In-process dispatch sets `IsInboxExecution` on the execution context
- **How to enable**: `AddLiteBusInboxInstrumentation()` plus the transport or in-process dispatch OpenTelemetry package for the dispatcher in use
- **Operational note**: Dual-hop AMQP patterns produce separate ingress and dispatch spans. Correlate them through the trace metadata on the accept

## References

- [Inbox](../../reliable-messaging/inbox.md)
- [Command module](../../concepts/commands.md)

## Test Coverage

### Covered Use Cases

#### `InboxTests.ProcessPendingAsync_ShouldExecuteCommandThroughMediatorAndMarkCompleted`

- **Use case**: Accept a command, then dispatch it in process
- **Test kind**: Unit
- **Description**: The full accept, processor, and handler chain in a test host
- **Behavior**: Accept the command and run a processor pass
- **Expected outcome**: The row completes and the handler runs
- **Remarks**: Core command-inbox pattern

#### `InboxTests.ProcessPendingAsync_ShouldSupportClosedGenericCommands`

- **Use case**: Closed generic command through the inbox path
- **Test kind**: Unit
- **Description**: A closed generic command type is registered and handled
- **Behavior**: Accept and process the closed generic command
- **Expected outcome**: The handler runs successfully
- **Remarks**:

#### `InProcessInboxDispatcherTests.DispatchAsync_ShouldExecuteCommandThroughMediator`

- **Use case**: The in-process dispatcher executes the command
- **Test kind**: Unit
- **Description**: A direct `IInboxDispatcher` invocation
- **Behavior**: `IInboxDispatcher` deserializes the envelope and calls the mediator
- **Expected outcome**: The command is handled
- **Remarks**:

#### `InProcessInboxDispatcherTests.DispatchAsync_WhenMessageIsNotACommand_ShouldThrowInvalidOperationException`

- **Use case**: A non-command message is rejected at dispatch
- **Test kind**: Unit
- **Description**: An event type stored in an inbox row (invalid for in-process command dispatch)
- **Behavior**: Dispatch the non-command envelope
- **Expected outcome**: InvalidOperationException
- **Remarks**:

#### `InProcessInboxDispatcherTests.DispatchAsync_ShouldSetIsInboxExecutionAndCopyTraceMetadata`

- **Use case**: The inbox execution context flag is set
- **Test kind**: Unit
- **Description**: Dispatch context propagation
- **Behavior**: In-process dispatch with trace metadata on the envelope
- **Expected outcome**: `IsInboxExecution` is true
- **Remarks**:

#### `CommandWithResultScheduledToInboxAnalyzerTests.ExplicitGenericAcceptAsyncWithCommandResult_ProducesDiagnostic`

- **Use case**: LB1004 blocks a command with a result in the inbox
- **Test kind**: Analyzer
- **Description**: Compile-time analysis of an `ICommand<TResult>` accept
- **Behavior**: An explicit generic accept with a command result type
- **Expected outcome**: Compile-time LB1004
- **Remarks**:

#### `AmqpInboxIngressEndToEndTests.PublishThroughRabbitMq_ShouldAcceptProcessAndDispatchCommand`

- **Use case**: AMQP ingress, accept, process, and dispatch
- **Test kind**: Integration
- **Description**: The chain from broker through ingress to processor
- **Behavior**: Publish the command to RabbitMQ, ingress accepts it, and the processor dispatches it
- **Expected outcome**: The command runs
- **Remarks**: Dual-hop entry

#### `PostgreSqlInboxEndToEndTests.ProcessPendingAsync_ShouldExecuteScheduledCommandThroughPostgreSqlStore`

- **Use case**: PostgreSQL command inbox end to end
- **Test kind**: Integration
- **Description**: A persistent store with in-process dispatch
- **Behavior**: Accept and process on a PostgreSQL-backed inbox
- **Expected outcome**: A completed row
- **Remarks**:

#### `InboxCompositeModuleTests.NestedConfiguration_ShouldAcceptAndProcessCommand`

- **Use case**: Accept and process through a nested module
- **Test kind**: Unit
- **Description**: A nested composite module registration
- **Behavior**: A nested inbox module configuration in a test host
- **Expected outcome**: The command runs end to end in the test host
- **Remarks**:

#### `TransportInboxDispatcherTests.DispatchAsync_ShouldPublishEnvelopeThroughTransport`

- **Use case**: Transport inbox dispatch publishes the envelope
- **Test kind**: Unit
- **Description**: The remote execution path through transport
- **Behavior**: The transport dispatcher publishes the leased envelope
- **Expected outcome**: The envelope reaches the transport
- **Remarks**: Dual-hop publish

#### `AmqpInboxDispatcherIntegrationTests.ProcessPendingAsync_ShouldPublishLeasedEnvelopeToAmqpQueue`

- **Use case**: AMQP inbox dispatch integration
- **Test kind**: Integration
- **Description**: A leased row published to an AMQP queue
- **Behavior**: A processor pass with the AMQP transport dispatcher
- **Expected outcome**: The message reaches the broker queue
- **Remarks**:

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Same-DB transactional accept of a follow-up command inside a handler | Yes | A rare pattern, covered only by generic transactional tests | Integration | Low |
| Query type accepted to inbox | No (by design) | No explicit rejection test beyond the command result case | Unit | Low |

### Out-of-Scope Use Cases

- Transparent attribute-driven inbox storage
- Request-response over the inbox
- Inbox storage of `ICommand<TResult>`
