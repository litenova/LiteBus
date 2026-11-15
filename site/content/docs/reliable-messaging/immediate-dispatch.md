# Immediate Dispatch

Some event handlers must take effect before the command's caller gets its answer, such as a read-model projection the next request reads. At the same time they must still be retried durably when they fail. Immediate dispatch runs those handlers in process right after the unit of work commits, and leaves anything it cannot finish to the outbox processor's normal retry.

> **This page is for** developers who need a committed event to take effect before the caller continues, without losing durable retry.
>
> **After reading it** you can mark handlers for immediate dispatch, choose between waiting and background modes, and monitor how often the processor fallback runs. For the outbox core, start with [Outbox](outbox.md).

## The Problem

A command commits domain state and stages an outbox envelope in one transaction. The background processor then publishes the envelope seconds later. That delay is fine for notifications, but not for a projection the very next request reads: the caller gets its answer before the read model reflects the write. The handler needs immediate consistency (its effect is visible when `SendAsync` returns) with eventual consistency as the fallback (a failure still retries durably until it succeeds).

## The Double-Run Workaround It Replaces

Applications build this today by publishing in-memory events after commit and letting the outbox publish them again: `IEventMediator.PublishAsync` runs the handlers at once for consistency, and the stored envelope runs them a second time through the processor for durability. That workaround runs every handler twice even when nothing fails, and each handler must tolerate a duplicate delivery on the healthy path, not only after a crash.

Immediate dispatch replaces the two publications with one path. After the commit, LiteBus dispatches the staged envelopes in process through the same envelope handling the processor uses. A successful dispatch marks the envelope published, so the processor never runs it again. A failed dispatch leaves the envelope to the processor's normal retry. When nothing fails each handler runs once.

## Setup

Immediate dispatch is opt-in per outbox module and needs per-handler delivery, so each handler gets its own envelope:

```csharp
services.AddLiteBus(builder =>
{
    builder.AddCommands(commands =>
    {
        commands.Register<SubmitOrderCommandHandler>();
        commands.UseUnitOfWork<OrderUnitOfWork>();
    });

    builder.AddEvents(events =>
    {
        events.Register<OrderProjectionHandler>();
        events.Register<OrderAuditHandler>();
    });

    builder.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<OrderSubmittedEvent>("orders.events.submitted", 1);
        outbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        outbox.UseInProcessDispatch();
        outbox.EnablePerHandlerDelivery();
        outbox.EnableImmediateDispatch(dispatch =>
        {
            dispatch.Timeout = TimeSpan.FromSeconds(10);
            dispatch.FallbackDelay = TimeSpan.FromSeconds(30);
        });
        outbox.EnableOutboxProcessor();
    });
});
```

Mark the handler whose effect must be visible at once with `[DispatchImmediately]`:

```csharp
[DispatchImmediately]
public sealed class OrderProjectionHandler : IEventHandler<OrderSubmittedEvent>
{
    public async Task HandleAsync(OrderSubmittedEvent message, CancellationToken cancellationToken = default)
    {
        await projectionStore.UpsertAsync(message.OrderId, cancellationToken).ConfigureAwait(false);
    }
}
```

Stage the event through `ITransactionalOutbox` inside the command handler, so the envelope joins the unit of work that produced it:

```csharp
public sealed class SubmitOrderCommandHandler : ICommandHandler<SubmitOrderCommand, Guid>
{
    public async Task<Guid> HandleAsync(SubmitOrderCommand command, CancellationToken cancellationToken = default)
    {
        await orders.SaveAsync(new Order(command.OrderId), cancellationToken).ConfigureAwait(false);
        await outbox.EnqueueAsync(
            OutboxEnqueueItem<OrderSubmittedEvent>.From(new OrderSubmittedEvent { OrderId = command.OrderId }),
            cancellationToken).ConfigureAwait(false);
        return command.OrderId;
    }
}
```

`EnableImmediateDispatch` requires a registered `IUnitOfWork`, an `IOutboxDispatcher`, and a store that implements `IOutboxTargetedLeaseStore` (the shipped PostgreSQL, Entity Framework Core, and in-memory stores do). Composition throws `LiteBusConfigurationException` when any of the three is missing. `FallbackDelay` must be greater than `Timeout`; anything else fails composition with `LiteBusConfigurationException`. It works with in-process dispatch and with transport dispatchers, which publish to the broker right after the commit.

Domain events collected on the aggregate and drained through `StageCollectedEvents` participate the same way, because staging runs through `ITransactionalOutbox`. See [Domain Events and Unit of Work](domain-events-and-unit-of-work.md).

## Which Envelopes Are Immediate

With per-handler delivery, an envelope is immediate when its target handler carries `[DispatchImmediately]`. Without per-handler delivery there is one envelope per event, and it is immediate when the event type carries the attribute. The attribute is class-level and not inherited. Envelopes with scheduled visibility (`MessageVisibility` other than immediate) are never immediate, and envelopes that are not immediate are untouched: they stay visible to the processor on the normal schedule.

Envelopes staged through the plain `IOutbox` writer are marked with the fallback-delay visibility but are never dispatched immediately, because that writer commits without a unit of work and therefore never raises a commit notification. The processor delivers them once the delay passes.

## Await Versus Background

`OutboxImmediateDispatchMode` decides whether the caller waits for the dispatch:

| Mode | Behavior | Use when |
| --- | --- | --- |
| `Await` (default) | `SendAsync` returns after the staged envelopes are dispatched or `Timeout` expires | The next request must read what the immediate handlers wrote |
| `Background` | Dispatch runs on a background task with its own scope after the commit, and the caller does not wait | The caller must return fast but the handlers should still usually run ahead of the processor poll |

In `Await` mode the caller's cancellation token is not used once the commit has happened; `Timeout` (default 10 seconds) bounds the wait. In both modes a failed envelope is persisted with the processor's retry visibility, a timed-out dispatch releases its remaining envelopes for the processor at once, and anything the dispatch never reaches stays invisible only until `FallbackDelay` passes. The command's outcome never changes because of immediate dispatch: its data is already committed.

## The Fallback Timeline

With the default `Timeout` of 10 seconds and `FallbackDelay` of 30 seconds, one commit behaves as follows:

1. During mediation, each transactional enqueue of an immediate envelope writes it with `VisibleAfter` set to now plus `FallbackDelay`, using the same clock source as other visibility writes. The background processor cannot lease it during that window. No schema change was needed: the column already exists.
2. The unit of work commits. The command's data is durable from here on.
3. After the commit, outside a batch, the unit-of-work completion handlers call every registered `IUnitOfWorkCommitListener` in registration order. A command batch calls them once after its single commit. A listener that throws is logged and does not fail the mediation.
4. The immediate dispatch listener takes the identifiers the mediation staged, leases them with `LeaseByIdAsync` regardless of `VisibleAfter`, and passes each leased envelope in staging order through the same envelope handling the processor uses, so success, failure, retry scheduling, dead-lettering, and telemetry follow the processor's rules.
5. When every envelope succeeds, each is marked published and the processor never runs it. Each healthy handler ran once.
6. When an envelope fails, it is persisted as failed with the processor's retry visibility, not held for the rest of `FallbackDelay`. The processor retries it on the normal schedule while the command stays succeeded.
7. When `Timeout` expires, dispatch stops and every leased envelope not yet dispatched is released as pending with `VisibleAfter` set to now, so the processor takes it at once instead of waiting out the delay.
8. When the process stops before dispatching, the staged envelopes become visible after `FallbackDelay` and the processor delivers them. Recovery takes at most the delay, with no operator action.

## Idempotency

Immediate dispatch is at-least-once, like the processor. In the worst case, a crash after a handler succeeded and before its envelope was marked published delivers the envelope again from the outbox, so the handler runs twice. Keep immediate handlers idempotent: key the projection write on the message id or a domain idempotency key, and make repeated runs converge to the same state. See [Reliable Messaging Semantics](semantics.md) for the guarantees each path provides.

## Monitoring the Fallback Rate

Immediate dispatch reuses the outbox meter, so `AddLiteBusOutboxMetrics()` from `LiteBus.Outbox.Extensions.OpenTelemetry` reports it with no package change:

| Instrument | Meaning |
| --- | --- |
| `litebus.outbox.immediate_dispatch.count` (`LiteBusOutboxTelemetry.ImmediateDispatchCountInstrumentName`) | One record per envelope handled, tagged with `litebus.outbox.immediate_dispatch.outcome`: `published`, `failed`, `skipped` (the lease could not claim it), or `timed_out` (released for the processor when the wait expired) |
| `litebus.outbox.immediate_dispatch.duration` (`LiteBusOutboxTelemetry.ImmediateDispatchDurationInstrumentName`) | Seconds per commit spent in immediate dispatch, recorded once per commit |

The fallback rate is the share of the count that is not `published`: `failed` envelopes went to processor retry, `skipped` envelopes were already claimed or settled elsewhere, and `timed_out` envelopes were released for the processor at once. A rising `failed` share points at handler or dispatcher errors; a rising `timed_out` share means `Timeout` is shorter than the dispatch work. A failed or timed-out envelope also logs a Warning naming the envelope id, contract name, and handler, so one fallback is traceable to its row.
