# Paged Fan-Out

A fan-out that pages every row inside one reaction holds the pipeline for the whole set. In the command case, it also holds a transaction.

Process one page per durable message instead. An inbox command carries the fan-out id and a cursor. Each dispatch handles N rows and accepts the next page. Every page is a separately leased message, so two instances never execute the same page concurrently.

## One Leased Message per Page

Make the fan-out unit a command. The handler pages N rows and dispatches the per-row work. It accepts the next page only when the page comes back full:

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

The inbox provides lease fencing. The inbox processor leases each page message to one worker, so a second instance skips a page another worker holds. Key each page accept on the fan-out id and cursor with `WithIdempotency`, so a retried page dispatch accepts no duplicate next page. Prefer keyset pagination over offsets, so rows inserted mid-fan-out do not shift any page boundary.

When each row's work must itself survive a crash, accept one inbox command per row instead of sending through the mediator. Per-row accepts are separately leased and retried messages. A mediator send inside the page handler fails with the page.

For event-shaped fan-out, the outbox counterpart (`OutboxEnqueueItem<TEvent>.WithIdempotency` plus `ScheduledAfter` for backoff between pages) follows the same pattern.

## Termination and Poison Pages

A page that returns fewer rows than requested, or a null next cursor, ends the fan-out. The handler returns without accepting another page.

A page that always throws exhausts the inbox retry schedule and dead-letters. Later pages are never accepted. A poison page therefore stops the fan-out where an operator can see it. Requeue the dead-lettered page to resume from its cursor.

Keep pages small enough that one page redelivers cheaply. Keep the per-row work idempotent, because at-least-once dispatch can run a page twice.

## Progress Visibility

The cursor already encodes progress, but nothing outside the handler can read it.

When operators need to watch a fan-out, correlate every page with `WithCorrelation` under the fan-out id. Keep saga state for the same correlation recording the last completed cursor (see [Saga](saga.md)). The saga row exists only for observability. The next-page accept stays the driver, so a lost saga write delays no page.

LiteBus ships no packaged page-handler contract. Cursor encoding, termination, and completion signalling stay application-owned. See [A Packaged Fan-Out Cursor](../roadmap/integrator-feedback.md#a-packaged-fan-out-cursor).

## Fanning Out from a Recurring Pass

When the fan-out is time-driven rather than event-driven, a recurring command replaces the self-re-enqueue driver. Each pass leases one batch of records from the store and accepts one inbox command per record. Every record then becomes its own leased message with its own retry and dead letter, instead of sharing one retry and dead letter for the whole page.

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

`HasMoreWork` drains a backlog in one busy period. A full batch sends the next pass immediately instead of waiting for the interval to pass. The pass therefore keeps leasing until a short batch reports the queue idle.

By default, the loop runs on every replica. Lease the rows with `FOR UPDATE SKIP LOCKED` semantics and let the store split the batch across instances. Alternatively, set `RequireLease` with a registered `IRecurringCommandLease`, so each pass runs on at most one replica (see [Recurring Commands](../integrations/recurring-commands.md)).

Either way, key each accept on the record id with `WithIdempotency`. A repeated pass then accepts no duplicate per-record command. Even when two instances race, a repeated pass is harmless.

To store the pass in one store round trip:

1. Build the per-record items with the typed factories.
2. Convert each with `InboxAcceptItem.From(item)`.
3. Accept the batch with `AcceptBatchAsync`.

## Next

- [Inbox](inbox.md) for lease, retry, and dead-letter behaviour
- [Saga](saga.md) for correlated progress state
- [Domain Events and Unit of Work](domain-events-and-unit-of-work.md) for the transaction boundary each page runs inside
- [Recurring Commands](../integrations/recurring-commands.md) for the poll loop that drives each pass
