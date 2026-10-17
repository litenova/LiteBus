# Recurring Commands

A recurring command sends one command on a fixed interval for the life of the host, without a hand-written hosted service. This page covers registration, options, backlog draining, failure behavior, and limitations.

**Package:** `LiteBus.Commands.Recurring`

## Poll Loop for Batch-Draining Commands

Polling loops that send one batch-draining command share one shape: send, loop immediately when the batch came back full, sleep when idle, back off on error, and never let an exception escape the loop. `AddRecurring` registers that loop so each job differs only in the command type, the timing values, and the declaration.

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

Each registration stores a `RecurringCommandDeclaration<TCommand>`: the per-pass command factory plus the loop options, resolved as a singleton so the background loop for one command type finds its own factory and options without positional matching. `AddRecurring` also registers the command type with the command module, so calling only this method is enough for the loop to send.

A command that implements more than one `ICommand<T>` contract fails composition at registration with `LiteBusConfigurationException`: a recurring command must declare at most one result type.

Each registration gets its own loop and its own options. The loop registers as an `IBackgroundService` through the host manifest, so it starts and stops with the host like every other LiteBus background service.

`AddRecurring` composes through `CommandModuleBuilder.RegisterChildModule`, the seam feature packages use to register inside `AddCommands` rather than as a second top-level module. Child modules are declared during `DeclareChildren` and built in dependency order; `LiteBus.Commands.Recurring` is the worked example of this pattern.

## Options

| Option | Default | Behavior |
| --- | --- | --- |
| `Enabled` | `true` | When false the loop logs once and never sends |
| `Interval` | 1 minute | Delay between sends when the previous send reported no more work |
| `StartupDelay` | `TimeSpan.Zero` | Delay after host start before the first send |
| `ErrorBackoff` | 5 minutes | Delay after a send throws, instead of `Interval` |
| `IsConcurrencyConflict` | `IsConcurrencyConflictByTypeName` | Predicate recognizing an optimistic-concurrency conflict; a conflict waits `Interval` instead of `ErrorBackoff`, and `null` treats every exception as a failure |

Timing values are validated when the module builds, so a negative interval fails at boot rather than at the first pass.

Stagger `StartupDelay` across loops that share a database, so a host start does not send every recurring command at once.

## Draining a Backlog

A result implementing `IRecurringWorkReport` makes the loop send again immediately instead of waiting out the interval, which clears a backlog in one busy period rather than one batch per interval:

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

A pass that throws is logged and the loop continues after `ErrorBackoff`, unless the `IsConcurrencyConflict` predicate classifies the exception as an optimistic-concurrency conflict: a conflict is logged at Information (EventId 2104) and the loop waits `Interval`, because another instance already did the work. The default predicate, `IsConcurrencyConflictByTypeName`, matches a type name ending in `ConcurrencyException` anywhere in the inner or aggregate exception chain; set the predicate to `null` to treat conflicts as failures. An exception never escapes the loop. A failing dependency is usually still failing one interval later, which is why the error delay is longer than the poll interval by default: it keeps a broken loop from filling the log at the polling rate.

Cancellation ends the loop immediately. The host's shutdown token is passed to the send, so an in-flight command observes it.

## Limitations

**It is not a scheduler.** There is no cron expression, no wall-clock alignment, no missed-run catch-up, and no notion of a run that was due while the host was down. If the work must happen at a particular time rather than regularly, use a scheduler.

**The loop runs on every host instance.** LiteBus has no leader election, so a deployment of three replicas sends the command three times per interval, concurrently. That is correct for a command that drains a queue-like table by leasing rows, because the store arbitrates and the instances share the work. It is wrong for a command that assumes it is alone.

Make the handler safe to run concurrently with itself, or do not use this. The usual shape that is safe:

```text
-- Each instance claims its own rows; the database decides who gets what.
UPDATE reminders SET claimed_by = @instance
WHERE id IN (
    SELECT id FROM reminders
    WHERE due_at <= now() AND claimed_by IS NULL
    ORDER BY due_at
    LIMIT @batchSize
    FOR UPDATE SKIP LOCKED
)
```

If the work genuinely must run once per interval across the deployment, the durable [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) processors already lease and fence per message, and are the right tool for that shape.

## Next

- [Commands](../concepts/commands.md) for the mediation pipeline each pass runs through
- [Outbox](../reliable-messaging/outbox.md) for lease-based processing with fencing across instances
