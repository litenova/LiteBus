# Reliable Messaging

LiteBus runs command, query, and event handlers in the caller's process by default. When accepted work must survive a crash, you store it first and let a background processor finish it. This page explains which durable axis fits each task: the inbox, the outbox, or a saga.

> **This page is for** developers who need accepted work to survive a process crash.
>
> **After reading it** you can pick the inbox, the outbox, or a saga for your task. Start with [Inbox](inbox.md).

## What Mediation Alone Does

For publish-subscribe, read the [Event Module](../concepts/events.md). For the stages all three message types share, read [The Handler Pipeline](../concepts/handler-pipeline.md).

In-process mediation runs handlers in the current call. If the process crashes while a handler runs, that work is lost. Durable messaging adds a store and a background processor with at-least-once delivery, leases, retries, and idempotency.

| Path | API | Execution | Survives a crash |
| --- | --- | --- | --- |
| In-process command | `ICommandMediator.SendAsync` | Now, in the caller | No |
| Durable message (inbox) | `IInbox.AcceptAsync` | Later, by `PipelinedInboxProcessor` | Yes |
| In-process event | `IEventMediator.PublishAsync` | Now, in the caller | No |
| Durable message (outbox) | `IOutbox.EnqueueAsync` | Later, by `PipelinedOutboxProcessor` | Yes |

The inbox and outbox are opt-in. Plain `SendAsync`, `QueryAsync`, and `PublishAsync` calls stay in process and never touch a durable store.

Durable APIs return a receipt that confirms storage, not a business result. When you need the outcome later, read it with a query or a read model.

Commands with results (`ICommand<TResult>`) cannot be stored in the inbox. Analyzer LB1004 reports this at compile time.

## Durable Axis Selection

| Axis | You store | A processor later | Use when |
| --- | --- | --- | --- |
| Inbox | A command through `IInbox.AcceptAsync` | `PipelinedInboxProcessor` executes it through `IInboxDispatcher` | Accepted work must run even if the process restarts |
| Outbox | An event through `IOutbox.EnqueueAsync` | `PipelinedOutboxProcessor` publishes it through `IOutboxDispatcher` | A state change must publish its event only when it commits |
| Saga | Correlated state through `EnableSaga` on the inbox | The inbox processor loads and saves state around dispatch | A multi-step workflow must track progress across commands |

The saga is not a workflow engine. It keeps durable state keyed by correlation. The inbox delivers commands at-least-once. It has no scheduler, timeout framework, or compensation language. See [Saga](saga.md).

## Required Registrations

A working durable path needs four registrations inside one module builder:

1. `AddInbox` or `AddOutbox` with contracts for every stored message type.
2. `Use*Storage` for the store the processor leases from.
3. `UseInProcessDispatch`, `UseAmqpDispatch`, or `UseInMemoryDispatch` for how leased rows reach handlers.
4. `EnableInboxProcessor` or `EnableOutboxProcessor` when the generic host should run the loops.

```csharp
builder.AddInbox(inbox =>
{
    inbox.Contracts.Register<ProcessPaymentCommand>(
        name: "payments.commands.process-payment",
        version: 1);

    inbox.UseInMemoryStorage();
    inbox.UseInProcessDispatch();
    inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
});
```

Storage, dispatch, and ingress register inside `AddInbox` and `AddOutbox` through `Use*` extensions. See [Dependency Graph](../architecture/dependency-graph.md) for package boundaries.

## How Delivery Behaves

LiteBus durable messaging provides at-least-once execution and publication, not exactly-once side effects. Processor pipelines are not atomic with external side effects by design. Combine the inbox and outbox with idempotent handlers and broker deduplication headers.

| Scenario | Risk | Mitigation |
| --- | --- | --- |
| Worker crash after handler runs, before terminal persist | Duplicate execution on lease reclaim | Idempotent handlers and `IdempotencyKey` on accept |
| Worker crash after broker publish, before outbox persist | Duplicate publication | Consumer deduplication and outbox idempotency key |
| Lease expiry during a slow handler | A second worker dispatches the same row | Heartbeat renewal and handler idempotency |
| In-memory stores | Rows stay in one process | Tests and single-node development only |
| In-memory transport consumer | Requeue when a handler throws, without broker dead letter | See [Architecture](../architecture/README.md) |

`HonorShutdownTokenOnPersist` defaults to `false`, so terminal persist completes on shutdown. Set it to `true` only with a documented operator drain policy. See [Reliable Messaging Semantics](semantics.md) for guarantees and [Ordering and Concurrency Guarantees](ordering.md) for what holds under leases.

## Keep State and Messages Together

`IInbox.AcceptAsync` and `IOutbox.EnqueueAsync` commit immediately. When a handler must persist domain state and inbox or outbox rows in one database transaction, use `ITransactionalInbox` and `ITransactionalOutbox` (PostgreSQL) or the generic variants over your `DbContext` (EF Core). See [Transactional Messaging Writes](transactional-writes.md) for registration, Marten enlistment, and API choice.

Every durable message needs a stable contract name and version registered on its module builder, or `[MessageContract]` with analyzer enforcement for production code. Declared command keys use `EnableIdempotency` (see [Idempotency](../concepts/idempotency.md)). Automatic mediator idempotency is a non-goal (see the [Idempotency Non-Goals](../catalog/durable-core/idempotency.md#non-goals)).

## Next

- [Durable Messaging Walkthrough](walkthrough.md)
- [Inbox](inbox.md)
- [Outbox](outbox.md)
- [Saga](saga.md)
