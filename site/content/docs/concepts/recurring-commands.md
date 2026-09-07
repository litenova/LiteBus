# Recurring Commands

**You will leave with:** how to send a command on an interval without writing a hosted service, what the loop guarantees, and the boundary this feature deliberately does not cross.

**Package:** `LiteBus.Commands.Recurring`

## The Problem

Every CQRS application grows a folder of hosted services that are the same file: create a scope, resolve `ICommandMediator`, send one command carrying a batch size, loop immediately when the batch came back full, sleep an idle interval otherwise, back off further on an exception, and never let one escape the loop. Ten of them differ only in the command type, two `TimeSpan` values, and a log message.

## Registration

```csharp
registry.AddCommands(commands => commands
    .RegisterFromAssembly(typeof(SendDueRemindersCommand).Assembly)
    .AddRecurring(
        () => new SendDueRemindersCommand { BatchSize = 50 },
        host =>
        {
            host.Interval = TimeSpan.FromMinutes(5);
            host.StartupDelay = TimeSpan.FromSeconds(30);
        }));
```

The factory is called once per pass, not once at registration, so a command carrying a timestamp or a fresh identifier gets a new one each time. A command with a public parameterless constructor can use the shorter overload:

```csharp
commands.AddRecurring<ExpireStaleHoldsCommand>(host => host.Interval = TimeSpan.FromMinutes(1));
```

Each registration gets its own loop and its own options. The loop registers as an `IBackgroundService` through the host manifest, so it starts and stops with the host like every other LiteBus background service.

## Options

| Option | Default | Behavior |
| --- | --- | --- |
| `Enabled` | `true` | When false the loop logs once and never sends |
| `Interval` | 1 minute | Delay between sends when the previous send reported no more work |
| `StartupDelay` | `TimeSpan.Zero` | Delay after host start before the first send |
| `ErrorBackoff` | 5 minutes | Delay after a send throws, instead of `Interval` |

Timing values are validated when the module builds, so a negative interval fails at boot rather than at the first pass.

Stagger `StartupDelay` across loops that share a database, so a host start does not send every recurring command at once.

## Draining a Backlog

A command that clears work in batches usually knows whether it filled the batch, and that is the difference between a backlog cleared in one interval and a backlog cleared in a hundred. A result implementing `IRecurringWorkReport` makes the loop send again immediately instead of waiting out the interval:

```csharp
public sealed record SendDueRemindersResult(int Sent, int BatchSize) : IRecurringWorkReport
{
    public bool HasMoreWork => Sent >= BatchSize;
}

public sealed class SendDueRemindersCommand : ICommand<SendDueRemindersResult>
{
    public required int BatchSize { get; init; }
}
```

Implementing it is optional. A command with no result, or one whose result does not implement it, waits the full interval between sends.

## Failure Behavior

A pass that throws is logged and the loop continues after `ErrorBackoff`. An exception never escapes the loop, because a background service that ends on the first transient database blip is a scheduled outage. A failing dependency is usually still failing one interval later, which is why the error delay is longer than the poll interval by default: it keeps a broken loop from filling the log at the polling rate.

Cancellation ends the loop immediately. The host's shutdown token is passed to the send, so an in-flight command observes it.

## What This Is Not

**It is not a scheduler.** There is no cron expression, no wall-clock alignment, no missed-run catch-up, and no notion of a run that was due while the host was down. If the work must happen at a particular time rather than regularly, use a scheduler.

**The loop runs on every host instance.** LiteBus has no leader election, so a deployment of three replicas sends the command three times per interval, concurrently. That is correct for a command that drains a queue-like table by leasing rows, because the store arbitrates and the instances share the work. It is wrong for a command that assumes it is alone.

Make the handler safe to run concurrently with itself, or do not use this. The usual shape that is safe:

```csharp
// Each instance claims its own rows; the database decides who gets what.
UPDATE reminders SET claimed_by = @instance
WHERE id IN (
    SELECT id FROM reminders
    WHERE due_at <= now() AND claimed_by IS NULL
    ORDER BY due_at
    LIMIT @batchSize
    FOR UPDATE SKIP LOCKED
)
```

If the work genuinely must run once per interval across the deployment, the durable [inbox](../reliable-messaging/inbox.md) and [outbox](../reliable-messaging/outbox.md) processors already lease and fence per message, and are the right tool for that shape.

## See Also

- [Commands](commands.md) for the mediation pipeline each pass runs through
- [Outbox](../reliable-messaging/outbox.md) for lease-based processing with fencing across instances
