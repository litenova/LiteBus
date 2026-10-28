# Processor Envelope Hooks

- **ID**: `durable-core.processor-hooks`
- **Name**: Processor Envelope Hooks
- **Maturity**: GA, with saga integration at Extension tier
- **Summary**: Processor envelope hooks let a feature run its own logic around one leased inbox or outbox envelope, without adding feature references to either durable axis.

## What It Does

Inbox and outbox processors adapt their envelopes to `IProcessorEnvelope` and invoke every registered `IProcessorEnvelopeHook` in dependency-injection registration order. The contract supports asynchronous state loading, synchronous ambient-scope attachment, post-dispatch persistence, and cleanup when processing cannot finish.

Saga uses these hooks to load and persist correlated state. Inbox and outbox packages reference `LiteBus.DurableMessaging.Abstractions`. They do not reference saga types.

## Hook Lifecycle

| Method | Invocation Point | Constraint |
| --- | --- | --- |
| `BeforeDispatchAsync` | After lease acquisition and before axis dispatch | The hook may perform asynchronous I/O such as saga state loading |
| `PrepareDispatchScope` | Immediately before the dispatcher call | This method is synchronous so `AsyncLocal` assignments occur on the dispatch flow |
| `ShouldDispatch` | After scope preparation, before the dispatcher call | Results combine with `&=` across hooks, and every hook is asked even after one declines. A `false` skips the dispatcher, but after-hooks still run and the success persists. |
| `AfterDispatchAsync` | After successful dispatch and before terminal persistence | This method runs on the persist token while the message lease remains active |
| `AbandonDispatchScope` | After cancellation, failed dispatch, or an interrupted after-hook sequence | This method releases in-memory state and must not persist business changes |

`PrepareDispatchScope`, `ShouldDispatch`, and `AbandonDispatchScope` have default implementations (`ShouldDispatch` defaults to `true`). A hook that owns ambient or keyed state implements scope preparation and abandonment together.

## Failure Semantics

Before-hook, prepare-hook, and dispatcher exceptions fail the current dispatch attempt. The processor invokes `AbandonDispatchScope` before it maps the failure to retry or dead letter.

An `AfterDispatchAsync` exception follows `ProcessorHookFailurePolicy`:

| Policy | Inbox or Outbox Result |
| --- | --- |
| `DeadLetter` | Persists a dead-letter terminal state and does not execute the handler or publisher again |
| `CompleteDespiteHookFailure` | Logs the hook failure and persists the successful completed or published state |

The runner invokes `AbandonDispatchScope` for every hook when an after-hook stops the sequence. This removes state owned by hooks that had not yet reached their normal cleanup path.

`HookFailurePolicy` is `null` by default, which means the dispatcher recommendation. An explicit value always takes precedence. Inbox resolves an unset policy to `DeadLetter`. In-process outbox dispatch also resolves to `DeadLetter`. Transport outbox dispatch resolves to `CompleteDespiteHookFailure` because the broker send has already succeeded and another send could duplicate the message.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `IProcessorEnvelopeHook` | `LiteBus.DurableMessaging.Abstractions` (`LiteBus.Messaging.Abstractions.Processing` namespace) | Axis-neutral lifecycle contract |
| `IProcessorEnvelope` | `LiteBus.DurableMessaging.Abstractions` (`LiteBus.Messaging.Abstractions.Processing` namespace) | Read-only message ID, contract, trace, and tenant metadata |
| `ProcessorHookFailurePolicy` | `LiteBus.DurableMessaging.Abstractions` (`LiteBus.Messaging.Abstractions.Processing` namespace) | Post-dispatch failure policy |
| `InboxProcessorOptions.HookFailurePolicy` | `LiteBus.Inbox.Abstractions` | Inbox policy selection |
| `OutboxProcessorOptions.HookFailurePolicy` | `LiteBus.Outbox.Abstractions` | Outbox policy selection |

## Registration

Register hook implementations through `IModuleConfiguration.DependencyRegistry`. Processors resolve `IEnumerable<IProcessorEnvelopeHook>` and preserve registration order.

Saga registration remains on the inbox builder:

```csharp
registry.AddInbox(inbox =>
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
| `LiteBus.DurableMessaging.Abstractions` | Hook and envelope contracts |
| `LiteBus.Inbox`, `LiteBus.Outbox` | Hook runners, envelope adapters, and failure-policy integration |
| `LiteBus.Saga` | Saga hook and execution context |
| `LiteBus.Saga.InboxIntegration` | Inbox builder and command mediation attachment |

## Requires

- Register the module through `runtime.module-configuration`.
- Use an inbox or outbox processor with hook invocation.

## Invariants

- Hooks run once per leased envelope, not once per processor pass.
- Hooks receive an adapted read-only envelope rather than a storage entity.
- Post-dispatch hooks run before terminal persistence while the lease is active.
- A failed dispatcher never causes saga state to persist for that attempt.
- Hook cleanup does not bypass store lease checks or alter terminal state.
- Inbox and outbox packages remain independent of saga packages.

## Non-Goals

- Hooks do not provide a transaction spanning external dispatch, hook persistence, and terminal store persistence.
- Hooks do not run on direct mediator calls outside durable processors.
- Hooks provide no automatic ordering beyond dependency-injection registration order.
- Hooks are not a workflow engine built from arbitrary processor hooks.
- Hooks do not provide saga integration on the outbox processor. Saga hooks run on the inbox processor only.

## Observability

Hooks use processor logs, counters, and activities. There is no hook-specific meter.

| Signal | Meaning |
| --- | --- |
| Inbox `failed` | Failure before successful dispatch, including failures in before or prepare hooks |
| Inbox `dead_lettered` | After-hook failure under `DeadLetter`, or an exhausted dispatch retry |
| Outbox `failed` | A publish failure before a terminal state |
| Outbox published terminal state with hook error log | `CompleteDespiteHookFailure` preserved a successful broker send |

## Test Coverage

| Test | Behavior |
| --- | --- |
| `InboxProcessorCorrectnessTests` | Before and after failures, dead-letter policy, complete-despite policy, no handler redispatch |
| `OutboxProcessorCorrectnessTests` | Published-state policy, dead-letter policy, no second publish |
| `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch` | Synchronous scope preparation reaches the dispatcher and state persists afterward |
| `SagaProcessorHookTests.ParallelDispatches_WithSameCorrelation_IsolateScopeByMessageId` | Concurrent messages for one correlation retain separate active scopes |
| EF Core and PostgreSQL after-dispatch integration tests | Hook failure transitions use real storage concurrency checks |

## References

- [Saga](../../reliable-messaging/saga.md)
- [Saga Processor Hook](../saga/processor-hook.md)
- [Inbox](../../reliable-messaging/inbox.md)
- [Outbox](../../reliable-messaging/outbox.md)
- [Architecture](../../architecture/README.md)
