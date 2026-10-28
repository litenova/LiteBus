# Processor Pipeline Coupling

- **ID**: `dispatch.processor-coupling`
- **Name**: Processor pipeline coupling
- **Maturity**: GA
- **Summary**: Pipelined inbox and outbox processors lease envelopes, invoke the registered dispatcher inside a per-message DI scope, and persist terminal outcomes from dispatch exceptions.

## What It Does

`PipelinedInboxProcessor` and `PipelinedOutboxProcessor` are the processor implementations. Each pass leases a batch, fans work out to `DispatcherConcurrency` workers, renews leases on a heartbeat interval, and calls `DispatchAsync` on the registered dispatcher. Dispatch failures throw. The processor maps exceptions to `Failed` or `DeadLettered` store state according to the retry policy.

Calling `EnableInboxProcessor()` or `EnableOutboxProcessor()` registers the background services through the module manifest. If no dispatcher is registered, startup fails. For each message, the processor resolves `IInboxDispatcher` or `IOutboxDispatcher` from a per-message scope, so scoped handlers (for example `DbContext`) stay isolated.

Hook failure policies differ by dispatcher type. Transport outbox dispatch defaults to `CompleteDespiteHookFailure`. In-process outbox and inbox processors default to `DeadLetter` when after-dispatch hooks fail.

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

- Dispatch is at-least-once. A crash between the external side effect and the terminal persist can produce duplicates.
- Outbox processors dispatch before the terminal published state is persisted. A broker acknowledgment without the matching persist means the message is republished on lease reclaim.
- Per-message dispatch failures do not abort sibling workers in the same pass. Pass-level abort applies to leasing and shutdown cancellation.
- A dispatch the processor cancels during shutdown, a drain, or a lease loss is released through `ILeaseRenewable.ReleaseLeaseAsync` with its attempt refunded instead of being recorded as a failure. The release runs on its own 5-second budget and falls back to lease expiry when it fails or times out.
- On graceful shutdown, in-flight rows may remain `Processing` until lease expiry unless drained.

## Non-Goals

- It does not implement exactly-once side effects or two-phase publish acknowledgment.
- It does not choose the dispatcher implementation. Registration is explicit.
- It does not return handler results to the original accept or enqueue caller.

## Public Surface

```csharp
inbox.UseProcessorOptions(new InboxProcessorOptions
{
    DispatcherConcurrency = 4,
    LeaseHeartbeatInterval = TimeSpan.FromSeconds(10)
});
inbox.EnableInboxProcessor();
```

### `IInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)`

| | |
| --- | --- |
| Package | `LiteBus.Inbox.Abstractions` |
| Called by | `PipelinedInboxProcessor` through a per-message DI scope |
| Contract | Throws on failure so the processor records a retry or dead-letter |

### `IOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Abstractions` |
| Called by | `PipelinedOutboxProcessor` through a per-message DI scope |
| Contract | Throws on failure so the processor records a retry or dead-letter |

### `PipelinedInboxProcessor` / `PipelinedOutboxProcessor`

| Member | Role |
| --- | --- |
| `ProcessPendingAsync(CancellationToken)` | Runs one processor pass, leasing a batch, running dispatch workers, and persisting outcomes |
| Constructor options | `InboxProcessorOptions` or `OutboxProcessorOptions` |

Key options affecting dispatch coupling:

| Option | Role |
| --- | --- |
| `DispatcherConcurrency` | The number of parallel dispatch workers per pass |
| `LeaseHeartbeatInterval` | Lease renewal during slow dispatch |
| `HookFailurePolicy` | The terminal state when after-dispatch hooks fail |
| `HonorShutdownTokenOnPersist` | Whether persist honors the shutdown token (a duplicate-dispatch trade-off) |

### `IProcessorEnvelopeHook`

These hooks run before and after each leased envelope (saga and custom hooks). Hook failures interact with `HookFailurePolicy` and with `IOutboxDispatcherModule.DefaultHookFailurePolicy` on transport outbox modules.

## Observability

| Signal | Name | When |
| --- | --- | --- |
| Inbox processor state | `litebus.inbox.processor.state` (Running, Paused, Draining) | Processor lifecycle transitions |
| Outbox processor state | `litebus.outbox.processor.state` | The same for outbox |
| Pass counters | `litebus.inbox.processor.passes`, `litebus.outbox.processor.passes` | At each processor pass start |
| Success counters | `litebus.inbox.processor.succeeded`, `litebus.outbox.processor.published` | Terminal success after dispatch and hooks |
| Failure counters | `litebus.inbox.processor.failed`, `litebus.outbox.processor.failed` | A dispatch or hook failure with a retry scheduled |
| Dead-letter counters | `litebus.inbox.processor.dead_lettered`, `litebus.outbox.processor.dead_lettered` | Maximum attempts or the hook dead-letter policy |
| Loop errors | `litebus.inbox.processor.loop_errors`, `litebus.outbox.processor.loop_errors` | On unexpected pass-level exceptions |
| Dispatch duration | `litebus.inbox.processor.dispatch_duration`, `litebus.outbox.processor.dispatch_duration` | A histogram around each `DispatchAsync` invocation |
| Lease lost | `litebus.inbox.processor.lease_lost`, `litebus.outbox.processor.lease_lost` | A heartbeat renewal failure cancels the in-flight dispatch |
| Lease released | EventId `3011` (Debug) | A cancelled dispatch returned to the queue with its attempt refunded |
| Lease release failed | EventId `3012` (Warning) | The release failed or timed out, so the lease expires and the row is reclaimed with the attempt counted |
| Transport send | `send {destination}` activity | Emitted by the concrete transport publisher during processor-driven dispatch |

There is no separate OpenTelemetry meter on `IInboxDispatcher` implementations. All dispatch-axis processor telemetry flows through the inbox and outbox processor meters.

## Analyzers

- **LB1014**: A processor enabled without a dispatcher in the same builder scope.

## References

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
- Returning handler or mediator results to the original accept or enqueue caller
