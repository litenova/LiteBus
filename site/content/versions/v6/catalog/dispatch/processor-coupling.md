# Processor Pipeline Coupling

- **ID**: `dispatch.processor-coupling`
- **Name**: Processor pipeline coupling
- **Maturity**: GA
- **Summary**: Pipelined inbox and outbox processors lease envelopes, invoke the registered dispatcher inside a per-message DI scope, and persist terminal outcomes from dispatch exceptions.

## What It Does

`PipelinedInboxProcessor` and `PipelinedOutboxProcessor` are the processor implementations. Each pass leases a batch, fans work to `DispatcherConcurrency` workers, renews leases on a heartbeat interval, and calls `DispatchAsync` on the registered dispatcher. Dispatch failures throw; the processor maps exceptions to `Failed` or `DeadLettered` store state according to retry policy.

Background services register through the module manifest when `EnableInboxProcessor()` or `EnableOutboxProcessor()` runs. Startup fails if no dispatcher is registered. Each dispatched message resolves `IInboxDispatcher` or `IOutboxDispatcher` from a per-message scope so scoped handlers (for example `DbContext`) are isolated.

Hook failure policies differ by dispatcher type. Transport outbox dispatch defaults to `CompleteDespiteHookFailure`; in-process outbox and inbox processors default to `DeadLetter` when after-dispatch hooks fail.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox` | `PipelinedInboxProcessor`, processor hosting |
| `LiteBus.Outbox` | `PipelinedOutboxProcessor`, processor hosting |
| `LiteBus.Inbox.Abstractions` | `IInboxDispatcher`, processor options |
| `LiteBus.Outbox.Abstractions` | `IOutboxDispatcher`, `IOutboxDispatcherModule.DefaultHookFailurePolicy` |
| `LiteBus.Messaging.Abstractions` | `ProcessorHookFailurePolicy`, hook contracts |

## Requires

- `durable-core.inbox.processor` or `durable-core.outbox.processor`
- `dispatch.registration` (a dispatcher must be registered)
- `durable-core.*.storage` (lease and state writers)

## Invariants

- At-least-once dispatch: crash between external side effect and terminal persist can produce duplicates.
- Outbox processors dispatch before terminal published state is persisted; broker ack without persist can republish on lease reclaim.
- Per-message dispatch failures do not abort sibling workers in the same pass; pass-level abort applies to leasing and shutdown cancellation.
- In-flight rows may remain `Processing` until lease expiry on graceful shutdown unless drained.

## Non-Goals

- Does not implement exactly-once side effects or two-phase publish acknowledgment.
- Does not choose the dispatcher implementation (registration is explicit).
- Does not return handler results to the original accept/enqueue caller.

## Public Surface

```csharp
inbox.EnableInboxProcessor(options =>
{
    options.DispatcherConcurrency = 4;
    options.LeaseHeartbeatInterval = TimeSpan.FromSeconds(10);
});
```

### `IInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)`

| | |
| --- | --- |
| Package | `LiteBus.Inbox.Abstractions` |
| Called by | `PipelinedInboxProcessor` via per-message DI scope |
| Contract | Throw on failure; processor records retry or dead-letter |

### `IOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Abstractions` |
| Called by | `PipelinedOutboxProcessor` via per-message DI scope |
| Contract | Throw on failure; processor records retry or dead-letter |

### `PipelinedInboxProcessor` / `PipelinedOutboxProcessor`

| Member | Role |
| --- | --- |
| `ProcessPendingAsync(CancellationToken)` | One processor pass: lease batch, dispatch workers, persist outcomes |
| Constructor options | `InboxProcessorOptions` / `OutboxProcessorOptions` |

Key options affecting dispatch coupling:

| Option | Role |
| --- | --- |
| `DispatcherConcurrency` | Parallel dispatch workers per pass |
| `LeaseHeartbeatInterval` | Lease renewal during slow dispatch |
| `HookFailurePolicy` | Terminal state when after-dispatch hooks fail |
| `HonorShutdownTokenOnPersist` | Whether persist honors shutdown token (duplicate-dispatch trade-off) |

### `IProcessorEnvelopeHook`

Before/after hooks around each leased envelope (saga, custom). Hook failures interact with `HookFailurePolicy` and `IOutboxDispatcherModule.DefaultHookFailurePolicy` on transport outbox modules.

## Observability

| Signal | Name | When |
| --- | --- | --- |
| Inbox processor state | `litebus.inbox.processor.state` (Running, Paused, Draining) | Processor lifecycle transitions |
| Outbox processor state | `litebus.outbox.processor.state` | Same for outbox |
| Pass counters | `litebus.inbox.processor.passes`, `litebus.outbox.processor.passes` | Each processor pass start |
| Success counters | `litebus.inbox.processor.succeeded`, `litebus.outbox.processor.published` | Terminal success after dispatch + hooks |
| Failure counters | `litebus.inbox.processor.failed`, `litebus.outbox.processor.failed` | Dispatch or hook failure with retry scheduled |
| Dead-letter counters | `litebus.inbox.processor.dead_lettered`, `litebus.outbox.processor.dead_lettered` | Max attempts or hook dead-letter policy |
| Loop errors | `litebus.inbox.processor.loop_errors`, `litebus.outbox.processor.loop_errors` | Unexpected pass-level exceptions |
| Dispatch duration | `litebus.inbox.processor.dispatch_duration`, `litebus.outbox.processor.dispatch_duration` | Histogram around each `DispatchAsync` invocation |
| Lease lost | `litebus.inbox.processor.lease_lost`, `litebus.outbox.processor.lease_lost` | Heartbeat renewal failure cancels in-flight dispatch |
| Transport send | `send {destination}` activity | Emitted by the concrete transport publisher during processor-driven dispatch |

No separate OpenTelemetry meter on `IInboxDispatcher` implementations; all dispatch-axis processor telemetry flows through inbox/outbox processor meters.

## Analyzers

- **LB1014**: Processor enabled without dispatcher in the same builder scope.

## Deep Docs

- [Architecture.md: Processor pipeline](../../architecture/README.md#processor-pipeline)
- [Inbox.md](../../reliable-messaging/inbox.md)
- [Outbox.md](../../reliable-messaging/outbox.md)
- [Reliable-Messaging-Semantics.md](../../reliable-messaging/semantics.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `PipelinedProcessor_WithConcurrencyOne_ShouldProcessAllCommands` | `LiteBus.Inbox.UnitTests` |
| `PipelinedProcessor_WithParallelWorkers_ShouldDispatchConcurrently` | `LiteBus.Inbox.UnitTests` |
| `PipelinedProcessor_WithHeartbeat_ShouldCompleteSlowHandlerWithoutReclaim` | `LiteBus.Inbox.UnitTests` |
| `PipelinedProcessor_when_after_dispatch_hook_fails_should_not_redispatch_handler` | `LiteBus.Inbox.UnitTests` |
| `PipelinedProcessor_when_after_dispatch_hook_fails_should_persist_dead_letter_without_completed` | `LiteBus.Inbox.UnitTests` |
| `PipelinedProcessor_when_hook_failure_policy_is_complete_despite_hook_failure_should_mark_completed` | `LiteBus.Inbox.UnitTests` |
| `PipelinedProcessor_when_lease_renewal_fails_should_cancel_dispatch` | `LiteBus.Inbox.UnitTests` |
| `ProcessPendingAsync_when_after_dispatch_hook_fails_should_dead_letter_from_processing` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `ProcessPendingAsync_parallel_workers_should_produce_single_terminal_state_per_message` | `LiteBus.Storage.IntegrationTests (`PostgreSql/`)` |
| `PipelinedProcessor_when_after_dispatch_hook_fails_should_not_redispatch` | `LiteBus.Outbox.UnitTests` |
| `PipelinedProcessor_when_after_dispatch_hook_fails_should_persist_dead_letter_without_published` | `LiteBus.Outbox.UnitTests` |
| `PipelinedProcessor_when_hook_failure_policy_is_complete_despite_hook_failure_should_mark_published` | `LiteBus.Outbox.UnitTests` |
| `ProcessPendingAsync_WhenPersistSkippedAfterPublish_ShouldRepublishOnRetry` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/InMemory/`) |
| `ProcessPendingAsync_WithConcurrentMessages_ShouldIsolateAndDisposeScopedDbContexts` | `LiteBus.Storage.IntegrationTests` (`EntityFrameworkCore/Inbox/`) |
| `ProcessPendingAsync_WhenShutdownBeginsAfterAmqpPublish_ShouldApplyTerminalPersistPolicy` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`) |
| `StopAsync_WhenInboxDispatchIsActive_ShouldWaitForCompletionAndPersistTerminalState` | `LiteBus.Runtime.UnitTests` (`Runtime/Hosting/`) |

### Out-of-Scope

- Exactly-once side effects or two-phase publish acknowledgment
- Automatic dispatcher selection (registration is explicit)
- Returning handler or mediator results to the original accept/enqueue caller
