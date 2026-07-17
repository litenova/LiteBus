# Processor Envelope Hooks

- **ID**: `durable-core.processor-hooks`
- **Name**: Processor Envelope Hooks
- **Maturity**: GA; saga integration is Extension tier
- **Summary**: Runs feature-owned logic around one leased inbox or outbox envelope without adding feature references to either durable axis.

## What It Does

Inbox and outbox processors adapt their envelopes to `IProcessorEnvelope` and invoke every registered `IProcessorEnvelopeHook` in dependency-injection registration order. The contract supports asynchronous state loading, synchronous ambient-scope attachment, post-dispatch persistence, and cleanup when processing cannot finish.

Saga uses this seam to load and persist correlated state. Inbox and outbox packages reference `LiteBus.Orchestration.Abstractions`; they do not reference saga types.

## Hook Lifecycle

| Method | Invocation Point | Constraint |
| --- | --- | --- |
| `BeforeDispatchAsync` | After lease acquisition and before axis dispatch | May perform asynchronous I/O such as saga state loading |
| `PrepareDispatchScope` | Immediately before the dispatcher call | Synchronous so `AsyncLocal` assignments occur on the dispatch flow |
| `AfterDispatchAsync` | After successful dispatch and before terminal persistence | Runs while the message lease remains active |
| `AbandonDispatchScope` | After cancellation, failed dispatch, or an interrupted after-hook sequence | Releases in-memory state; must not persist business changes |

`PrepareDispatchScope` and `AbandonDispatchScope` have default no-op implementations. A hook that owns ambient or keyed state implements both methods as a pair.

## Failure Semantics

Before-hook, prepare-hook, and dispatcher exceptions fail the current dispatch attempt. The processor invokes `AbandonDispatchScope` before it maps the failure to retry or dead letter.

An `AfterDispatchAsync` exception follows `ProcessorHookFailurePolicy`:

| Policy | Inbox or Outbox Result |
| --- | --- |
| `DeadLetter` | Persist a dead-letter terminal state; do not execute the handler or publisher again |
| `CompleteDespiteHookFailure` | Log the hook failure and persist the successful completed or published state |

The runner invokes `AbandonDispatchScope` for every hook when an after-hook stops the sequence. This removes state owned by hooks that had not yet reached their normal cleanup path.

Inbox defaults to `DeadLetter`. In-process outbox dispatch also defaults to `DeadLetter`. Transport outbox dispatch defaults to `CompleteDespiteHookFailure` because the broker send has already succeeded and another send could duplicate the message.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `IProcessorEnvelopeHook` | `LiteBus.Orchestration.Abstractions` | Axis-neutral lifecycle contract |
| `IProcessorEnvelope` | `LiteBus.Orchestration.Abstractions` | Read-only message ID, contract, trace, and tenant metadata |
| `ProcessorHookFailurePolicy` | `LiteBus.Messaging.Abstractions` | Post-dispatch failure policy |
| `InboxProcessorOptions.HookFailurePolicy` | `LiteBus.Inbox.Abstractions` | Inbox policy selection |
| `OutboxProcessorOptions.HookFailurePolicy` | `LiteBus.Outbox.Abstractions` | Outbox policy selection |

## Registration

Register hook implementations through `IModuleConfiguration.DependencyRegistry`. Processors resolve `IEnumerable<IProcessorEnvelopeHook>` and preserve registration order.

Saga registration remains on the inbox builder:

```csharp
registry.AddInboxModule(inbox =>
{
    inbox.EnableSaga(saga =>
    {
        saga.MapState<OrderSagaState>("orders.saga.advance");
    });
});
```

`EnableSaga` registers `SagaProcessorHook` as a singleton hook. `SagaExecutionContext` keys active entries by `IProcessorEnvelope.MessageId`, while persisted state remains keyed by `SagaCorrelation`.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Orchestration.Abstractions` | Hook and envelope contracts |
| `LiteBus.Inbox`, `LiteBus.Outbox` | Hook runners, envelope adapters, and failure-policy integration |
| `LiteBus.Saga` | Saga hook and execution context |
| `LiteBus.Saga.InboxIntegration` | Inbox builder and command mediation attachment |

## Invariants

- Hooks run once per leased envelope, not once per processor pass.
- Hooks receive an adapted read-only envelope rather than a storage entity.
- Post-dispatch hooks run before terminal persistence while the lease is active.
- A failed dispatcher never causes saga state to persist for that attempt.
- Hook cleanup does not bypass store lease checks or alter terminal state.
- Inbox and outbox packages remain independent of saga packages.

## Non-Goals

- A transaction spanning external dispatch, hook persistence, and terminal store persistence.
- Hook execution on direct mediator calls outside durable processors.
- Automatic ordering beyond dependency-injection registration order.
- A workflow engine built from arbitrary processor hooks.

## Observability

Hooks use processor logs, counters, and activities. There is no hook-specific meter.

| Signal | Meaning |
| --- | --- |
| Inbox `failed` | Failure before successful dispatch, including before or prepare hooks |
| Inbox `dead_lettered` | After-hook failure under `DeadLetter`, or exhausted dispatch retry |
| Outbox `failed` | Publish failure before a terminal state |
| Outbox published terminal state with hook error log | `CompleteDespiteHookFailure` preserved a successful broker send |

## Test Coverage

| Test | Behavior |
| --- | --- |
| `InboxProcessorCorrectnessTests` | Before and after failures, dead-letter policy, complete-despite policy, no handler redispatch |
| `OutboxProcessorCorrectnessTests` | Published-state policy, dead-letter policy, no second publish |
| `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch` | Synchronous scope preparation reaches the dispatcher and state persists afterward |
| `SagaProcessorHookTests.ParallelDispatches_WithSameCorrelation_IsolateScopeByMessageId` | Concurrent messages for one correlation retain separate active scopes |
| EF Core and PostgreSQL after-dispatch integration tests | Hook failure transitions use real storage concurrency checks |

## Deep Docs

- [Saga](../../reliable-messaging/saga.md)
- [Inbox](../../reliable-messaging/inbox.md)
- [Outbox](../../reliable-messaging/outbox.md)
- [Architecture](../../architecture/README.md)
