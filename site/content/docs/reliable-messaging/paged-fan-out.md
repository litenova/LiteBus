# Paged Fan-Out

A fan-out that pages every row inside one reaction or command holds the pipeline, and in the command case a transaction, for the whole set. Process one page per durable message instead: an inbox command carries the fan-out id and a cursor, each dispatch handles N rows and accepts the next page, and every page is a separately leased message, so two instances never execute the same page concurrently.

## One Leased Message per Page

Make the fan-out unit a command. The handler pages N rows, dispatches the per-row work, and accepts the next page only when the page comes back full:

```csharp
public sealed record NotifyBuyersOfMoveCommand(Guid EventId, string? Cursor) : ICommand;

public sealed class NotifyBuyersOfMoveCommandHandler : ICommandHandler<NotifyBuyersOfMoveCommand>
{
    private const int PageSize = 100;

    private readonly IOrderStore _orders;
    private readonly ICommandMediator _commands;
    private readonly IInbox _inbox;

    public NotifyBuyersOfMoveCommandHandler(IOrderStore orders, ICommandMediator commands, IInbox inbox)
    {
        _orders = orders;
        _commands = commands;
        _inbox = inbox;
    }

    public async Task HandleAsync(NotifyBuyersOfMoveCommand command, CancellationToken cancellationToken = default)
    {
        var page = await _orders.ListConfirmedAsync(command.EventId, command.Cursor, PageSize, cancellationToken).ConfigureAwait(false);

        foreach (var order in page.Orders)
        {
            await _commands.SendAsync(new NotifyBuyerOfMoveCommand(order.Id), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (page.NextCursor is null)
        {
            return;
        }

        await _inbox.AcceptAsync(
            InboxAcceptItem<NotifyBuyersOfMoveCommand>.WithIdempotency(
                new NotifyBuyersOfMoveCommand(command.EventId, page.NextCursor),
                $"fanout:{command.EventId}:{page.NextCursor}"),
            cancellationToken).ConfigureAwait(false);
    }
}
```

Lease fencing comes free with the axis: the inbox processor leases each page message to one worker, so a second instance skips a page another holds. Key each page accept on the fan-out id and cursor with `WithIdempotency`, so a retried page dispatch accepts no duplicate next page. Prefer keyset pagination over offsets, so rows inserted mid-fan-out shift no page boundary.

When each row's work must itself survive a crash, accept one inbox command per row instead of sending through the mediator: per-row accepts are separately leased and retried messages, while a mediator send inside the page handler dies with the page. For event-shaped fan-out, the outbox twin (`OutboxEnqueueItem<TEvent>.WithIdempotency` plus `ScheduledAfter` for backoff between pages) follows the same shape.

## Termination and Poison Pages

A page that returns fewer rows than requested, or a null next cursor, ends the fan-out: the handler returns without accepting another page. A page that always throws exhausts the inbox retry schedule and dead-letters, and later pages are never accepted, so a poison page parks the fan-out where an operator can see it; requeue the dead-lettered page to resume from its cursor. Keep pages small enough that one page redelivers cheaply, and keep the per-row work idempotent, because at-least-once dispatch can run a page twice.

## Progress Visibility

The cursor already encodes progress, but nothing outside the handler can read it. When operators need to watch a fan-out, correlate every page with `WithCorrelation` under the fan-out id and keep saga state for the same correlation recording the last completed cursor (see [Saga](saga.md)). The saga row is observability only; the next-page accept stays the driver, so a lost saga write delays no page.

A packaged page-handler contract is not shipped; cursor encoding, termination, and completion signalling stay application-owned. See [A Packaged Fan-Out Cursor](../roadmap/integrator-feedback.md#a-packaged-fan-out-cursor).

## Fanning Out From a Recurring Pass

When the fan-out is time-driven rather than event-driven, a recurring command replaces the self-re-enqueue driver: each pass leases one batch of records from the store and accepts one inbox command per record, so every record becomes its own leased message with its own retry and dead letter instead of sharing the fate of the page.

```csharp
public sealed record SweepDueRemindersCommand(int BatchSize) : ICommand<SweepDueRemindersResult>;

public sealed record SweepDueRemindersResult(int Accepted, int BatchSize) : IRecurringWorkReport
{
    public bool HasMoreWork => Accepted >= BatchSize;
}

public sealed class SweepDueRemindersCommandHandler : ICommandHandler<SweepDueRemindersCommand, SweepDueRemindersResult>
{
    private readonly IReminderStore _reminders;
    private readonly IInbox _inbox;

    public SweepDueRemindersCommandHandler(IReminderStore reminders, IInbox inbox)
    {
        _reminders = reminders;
        _inbox = inbox;
    }

    public async Task<SweepDueRemindersResult> HandleAsync(SweepDueRemindersCommand command, CancellationToken cancellationToken = default)
    {
        var due = await _reminders.LeaseDueAsync(command.BatchSize, cancellationToken).ConfigureAwait(false);

        foreach (var reminder in due)
        {
            await _inbox.AcceptAsync(
                InboxAcceptItem<SendReminderCommand>.WithIdempotency(
                    new SendReminderCommand(reminder.Id),
                    $"reminder:{reminder.Id}"),
                cancellationToken).ConfigureAwait(false);
        }

        return new SweepDueRemindersResult(due.Count, command.BatchSize);
    }
}
```

```csharp
registry.AddCommands(commands => commands
    .AddRecurring(
        () => new SweepDueRemindersCommand(BatchSize: 100),
        host =>
        {
            host.Interval = TimeSpan.FromMinutes(5);
            host.RequireLease = true;
        }));
```

`HasMoreWork` drains a backlog in one busy period: a full batch sends the next pass immediately instead of waiting out the interval, so the pass keeps leasing until a short batch reports the queue idle. By default the loop runs on every replica, so either lease the rows with `FOR UPDATE SKIP LOCKED` semantics and let the store split the batch across instances, or set `RequireLease` with a registered `IRecurringCommandLease` so each pass runs on at most one replica (see [Recurring Commands](../integrations/recurring-commands.md)). Either way, key each accept on the record id with `WithIdempotency`, so a repeated pass accepts no duplicate per-record command; a repeated pass is then harmless even when two instances race. Build the per-record items with the typed factories, convert each with `InboxAcceptItem.From(item)`, and accept the batch with `AcceptBatchAsync` to store the pass in one store round trip.

## Next

- [Inbox](inbox.md) for lease, retry, and dead-letter behaviour
- [Saga](saga.md) for correlated progress state
- [Domain Events and Unit of Work](domain-events-and-unit-of-work.md) for the transaction boundary each page runs inside
- [Recurring Commands](../integrations/recurring-commands.md) for the poll loop that drives each pass
