# Recurring Commands

A recurring command sends one command on a fixed interval for the life of the host, without a hand-written hosted service. This page covers registration, options, backlog draining, failure behavior, and limitations.

**Package:** `LiteBus.Commands.Recurring`

**Lease package:** `LiteBus.Commands.Recurring.Storage.PostgreSql` (opt-in single-flight)

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
| `RequireLease` | `false` | When true each pass first acquires the named lease; a pass that cannot acquire it is skipped at Debug (EventId 2105) and the loop waits `Interval` |
| `LeaseName` | Command type full name | The lease name acquired when `RequireLease` is true; loops sharing a name single-flight against each other |

Timing values are validated when the module builds, so a zero or negative interval or error backoff fails at boot rather than at the first pass. Zero would remove pacing between passes: the failure case would log EventId 2103 in a tight loop while a dependency is down, and the success case would send at an unbounded rate. The loop snapshots the validated timing values when it starts, so changing the options afterwards does not change the pacing of a running loop.

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

## Running on Several Replicas

By default the loop runs on every host instance. A loop that must run on at most one instance at a time opts into single-flight with `RequireLease` and a registered `IRecurringCommandLease`:

```csharp
registry.AddCommands(commands => commands
    .RegisterFromAssembly(typeof(SendDueRemindersCommand).Assembly)
    .UsePostgreSqlRecurringLease(postgres =>
        postgres.UseConnectionString(configuration.GetConnectionString("OrdersDb")!))
    .AddRecurring(
        () => new SendDueRemindersCommand { BatchSize = 50 },
        host => host.RequireLease = true));
```

When `RequireLease` is true, each pass first tries to acquire the named lease; a pass that cannot acquire it is skipped and logged at Debug (EventId 2105), and the loop waits `Interval`. One acquired lease covers the whole busy period: immediate re-sends driven by `IRecurringWorkReport.HasMoreWork` reuse it, and it is released before the loop waits. Composition fails with `LiteBusConfigurationException` when `RequireLease` is set and no `IRecurringCommandLease` is registered. `LeaseName` selects the lease name and defaults to the command type's full name; loops that share a name single-flight against each other, so one explicit name can serialize two loops that must never overlap. There is no standing leader: every pass races for the lease, so the instance that runs varies from pass to pass. The lease can be lost mid-pass when another holder takes the row or renewals fail: the handle cancels its `LeaseLost` token, the loop ends the busy period, releases the handle, and waits `ErrorBackoff` instead of sending further passes, and the in-flight send runs under a token linked to the loss signal so a handler that observes cancellation stops with the pass. The loss is logged at Warning (EventId 2106).

`LiteBus.Commands.Recurring.Storage.PostgreSql` implements the lease as one renewable row per name in the `litebus_recurring_leases` table: each acquisition is a single upsert that inserts the row or takes it over when its expiry has passed on the database clock, returning a row only to the winner, and the holder identity recorded on the row is the machine name plus the process id plus a per-instance GUID so operators can see which replica holds each lease.

```csharp
postgres.UseOptions(new PostgreSqlRecurringLeaseOptions
{
    SchemaName = "public",
    TableName = "litebus_recurring_leases",
    LeaseDuration = TimeSpan.FromSeconds(30),
    RenewInterval = TimeSpan.FromSeconds(10)
});
```

The handle renews its row in the background every `RenewInterval` (default one third of `LeaseDuration`, which defaults to 30 seconds) and deletes it on dispose when this holder still owns it; a holder whose process dies stops renewing, so its row becomes takeable once the duration lapses, which bounds how long a crashed replica blocks the loop. A renewal that fails or finds the lease taken stops renewing, cancels the handle's `LeaseLost` token so the pass holding it ends its busy period, and logs a warning (EventId 2110 when stolen, 2111 when the renewal itself fails), leaving the row for its new holder or for expiry. Set an explicit `RenewInterval` below `LeaseDuration`; a value at or above the duration fails validation with `ArgumentOutOfRangeException`, because the first renewal would land after the lease lapses. Size `LeaseDuration` from how quickly a replacement pass must run after a crash: a short duration frees a dead holder faster but narrows the margin a live holder has against a stalled renewal, while a long duration tolerates stalls but keeps a dead holder blocking longer. Every statement opens its own pooled connection and no connection is held between statements, so the lease works behind PgBouncer in transaction mode and with pooling disabled.

Operators inspect current holders with a plain query:

```sql
SELECT lease_name, holder, acquired_at, expires_at, renewed_at FROM litebus_recurring_leases ORDER BY lease_name;
```

The module creates the table at host startup by default and reports its shape through the `recurring.postgresql.schema` diagnostic probe; disable startup creation with `DisableSchemaInitialization` when a migration pipeline owns the table.

There is a second pattern that needs no lease package and works with any store: keep the recurring loop as a trigger that only writes a durable message, and put the exactly-once guarantee on the message the pass writes. A pass that enqueues one outbox event, or accepts one idempotent inbox command, can run on every replica; the outbox and inbox processors already lease and fence per message, so the work happens once however many loops wrote it. Prefer this shape when the work already flows through the inbox or the outbox, and reserve `RequireLease` for loops whose pass does its work inline.

## Limitations

**It is not a scheduler.** There is no cron expression, no wall-clock alignment, no missed-run catch-up, and no notion of a run that was due while the host was down. If the work must happen at a particular time rather than regularly, use a scheduler.

**The loop runs on every host instance by default.** A deployment of three replicas sends the command three times per interval, concurrently. That is correct for a command that drains a queue-like table by leasing rows, because the store arbitrates and the instances share the work. A loop that must run on at most one instance at a time opts into single-flight; see [Running on Several Replicas](#running-on-several-replicas).

Make the handler safe to run concurrently with itself, require the lease, or do not use this. The usual shape that is safe:

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

If the work genuinely must run once per interval across the deployment, either require the lease as described above or route the work through the durable [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) processors, which already lease and fence per message.

## Next

- [Commands](../concepts/commands.md) for the mediation pipeline each pass runs through
- [Outbox](../reliable-messaging/outbox.md) for lease-based processing with fencing across instances
