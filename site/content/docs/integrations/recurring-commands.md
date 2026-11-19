# Recurring Commands

A recurring command sends one command on a fixed interval for the life of the host, without a hand-written hosted service. When each pass produces several commands, a recurring batch sends them as one atomic batch by default, or one at a time with `RecurringBatchIsolation.PerItem`. A per-tenant sweep always sends one command per tenant with that tenant's identity, like a per-item batch, and a schedule aligns passes to wall-clock occurrences instead of a fixed interval. This page covers registration, identity, tenant sweeps, schedules, options, backlog draining, failure behavior, and limitations.

The registration contract and failure modes are documented on `CommandModuleBuilderRecurringExtensions.AddRecurring`, `AddRecurringBatch`, `AddRecurringForEachTenant`, `IRecurringTenantSource`, `IRecurringSchedule`, `RecurringSchedules`, and `RecurringCommandHostOptions`. This page covers tuning and operations: option sizing, backlog draining, failure signals, lease operations, and SQL examples.

**Package:** `LiteBus.Commands.Recurring`

**Lease package:** `LiteBus.Commands.Recurring.Storage.PostgreSql` (opt-in single-flight)

## Poll Loop for Batch-Draining Commands

`AddRecurring` registers the poll loop that sends one batch-draining command; the loop contract is documented on `CommandModuleBuilderRecurringExtensions.AddRecurring`. Size the command's batch so a pass completes well within `Interval`, and report a full batch through `IRecurringWorkReport` so a backlog drains without idle waits between passes.

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

The factory timing contract is documented on `AddRecurring`. A command with a public parameterless constructor can use the shorter overload:

```csharp
commands.AddRecurring<ExpireStaleHoldsCommand>(host => host.Interval = TimeSpan.FromMinutes(1));
```

Each registration stores a `RecurringCommandDeclaration<TCommand>`. It holds the per-pass command factory, the loop options, and the optional identity factory, resolved as a singleton, so the background loop for one command type finds its own factory and options without positional matching. See `AddRecurring` for the command registration contract.

## Sending with an Identity

The loop runs outside any mediation, so without an identity the pass mediation resolves the identity from the registered `IExecutionIdentityProvider` or falls back to `ExecutionIdentity.Anonymous`. A tenant-required recurring command would always be denied that way. Pass an identity factory when the command needs an identity the loop cannot inherit:

```csharp
registry.AddCommands(commands => commands
    .RegisterFromAssembly(typeof(CloseStaleCartsCommand).Assembly)
    .AddRecurring(
        () => new CloseStaleCartsCommand { TenantCode = "acme" },
        host => host.Interval = TimeSpan.FromMinutes(5),
        identity: command => ExecutionIdentity.ForTenant(command.TenantCode)));
```

The factory receives the command the pass built, so the identity can derive from per-pass values such as a tenant code carried on the command. The loop sends with `CommandMediationSettings { Identity = ... }`. Both `AddRecurring` overloads accept the optional `identity` parameter, and `RecurringCommandDeclaration<TCommand>.IdentityOf` carries it to the loop. Atomic batch items share one identity (or carry none), per-item batch items each carry their own identity, and tenant sweeps send with the tenant identity; see [Sending a Batch Each Pass](#sending-a-batch-each-pass) and [Sending One Command per Tenant](#sending-one-command-per-tenant).

A command declaring more than one `ICommand<T>` contract fails composition; see `AddRecurring` for the single-contract requirement.

Each registration runs as its own `IBackgroundService` through the host manifest, so it starts and stops with the host like every other LiteBus background service. See `AddRecurring` for the per-registration loop contract.

`AddRecurring` composes through `CommandModuleBuilder.RegisterChildModule`, the seam feature packages use to register inside `AddCommands` rather than as a second top-level module. Child modules are declared during `DeclareChildren` and built in dependency order. `LiteBus.Commands.Recurring` is the worked example of this pattern.

## Options

| Option | Default | Behavior |
| --- | --- | --- |
| `Enabled` | `true` | When false the loop logs once and never sends |
| `Interval` | 1 minute | Delay between sends when the previous send reported no more work |
| `Schedule` | `null` | When set, the loop waits for the next occurrence from the schedule before the first pass and between passes, instead of `Interval`. `null` keeps the fixed-interval wait |
| `StartupDelay` | `TimeSpan.Zero` | Delay after host start before the first send |
| `ErrorBackoff` | 5 minutes | Delay after a send throws, instead of `Interval` |
| `IsConcurrencyConflict` | `IsConcurrencyOrDatabaseConflict` | Predicate recognizing a lost race: an optimistic-concurrency conflict, a deadlock, or a serialization failure. A conflict waits `Interval` instead of `ErrorBackoff`, and `null` treats every exception as a failure. |
| `RequireLease` | `false` | When true, each pass first acquires the named lease. A pass that cannot acquire it is skipped at Debug (EventId 2105) and the loop waits `Interval`. |
| `LeaseName` | Command type full name | The lease name acquired when `RequireLease` is true. Loops sharing a name single-flight against each other. |

Timing values are validated when the module builds, so a zero or negative interval or error backoff fails at boot rather than at the first pass. Zero would remove pacing between passes. The failure case would log EventId 2103 in a tight loop while a dependency is down, and the success case would send at an unbounded rate. The loop snapshots the validated timing values when it starts, so changing the options afterwards does not change the pacing of a running loop.

Stagger `StartupDelay` across loops that share a database, so a host start does not send every recurring command at once.

## Draining a Backlog

Report a full batch through `IRecurringWorkReport` so the loop drains without idle waits; see `AddRecurring` and `IRecurringWorkReport` for the re-send contract:

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

Reporting is optional. A command with no result, or one whose result does not implement the report, waits the full interval between sends.

## Sending a Batch Each Pass

When each pass produces several commands, implement `IRecurringCommandBatchSource` and register it with `AddRecurringBatch`. Each pass resolves the source from a fresh dispatch scope, calls `CreateBatchAsync`, disposes the scope, and then sends the returned batch according to `RecurringCommandBatch.Isolation`. `RecurringBatchIsolation.Atomic`, the default, sends the items as one all-or-nothing batch through `ICommandBatchMediator.TrySendManyAsync`. `RecurringBatchIsolation.PerItem` sends the items one at a time, in list order, each with its own identity through `ICommandMediator.SendAsync`. The source can take scoped dependencies such as a database context. See `AddRecurringBatch` for the batch loop contract.

The batch holds `IReadOnlyList<RecurringCommandItem> Items`, where `RecurringCommandItem(ICommand Command, ExecutionIdentity? Identity = null)` pairs one command with the identity its pass sends it for, and `RecurringBatchIsolation Isolation`, which defaults to `RecurringBatchIsolation.Atomic`. `RecurringCommandBatch.From(IReadOnlyList<ICommand>)` stays as the plain-command helper that assigns no identity, and `RecurringCommandBatch.From(IReadOnlyList<RecurringCommandItem>)` takes an optional `isolation` parameter that defaults to `RecurringBatchIsolation.Atomic`. Pass `RecurringBatchIsolation.PerItem` when the items carry different identities:

```csharp
public async Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
{
    var due = await _context.Invoices
        .Where(invoice => invoice.DueDate <= DateTimeOffset.UtcNow)
        .Take(50)
        .ToListAsync(cancellationToken)
        .ConfigureAwait(false);

    return RecurringCommandBatch.From(
        due.Select(static invoice => new RecurringCommandItem(
            new SendInvoiceReminderCommand(invoice.Id),
            ExecutionIdentity.ForTenant(invoice.TenantCode))).ToList(),
        due.Count >= 50,
        RecurringBatchIsolation.PerItem);
}
```

An atomic batch runs its commands one at a time, in list order, inside one dispatch scope with one unit-of-work commit, and the first denied, invalid, faulted, or cancelled command rolls the whole batch back. Every item of an atomic batch must carry the same identity, or none; a batch that mixes identities fails the pass with `InvalidOperationException`, which names `RecurringBatchIsolation.PerItem` as the way to send each item with its own identity. A refusal that rolls the batch back is logged once at Warning (EventId 2107) and the pass waits `ErrorBackoff` instead of the interval, so a command refused on every pass does not spin the loop. A per-item batch instead sends each item as its own mediation, so one failing item is logged and counted and never stops the rest of the batch: a refused item is logged at Warning (EventId 2113), an item whose exception `IsConcurrencyConflict` classifies as a lost race is logged at Information (EventId 2104), and any other failure is logged at Error (EventId 2108). When any item failed or was refused, the batch summary is logged at Warning (EventId 2109) and the pass waits `ErrorBackoff` instead of the interval. A command that needs the value another command produces does not belong in a batch, because items cannot see each other's results and a result-bearing command is sent without reading its result.

```csharp
public sealed class DueInvoiceBatchSource : IRecurringCommandBatchSource
{
    private readonly BillingContext _context;

    public DueInvoiceBatchSource(BillingContext context)
    {
        _context = context;
    }

    public async Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
    {
        var due = await _context.Invoices
            .Where(invoice => invoice.DueDate <= DateTimeOffset.UtcNow)
            .Take(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return RecurringCommandBatch.From(
            due.Select(static invoice => (ICommand)new SendInvoiceReminderCommand(invoice.Id)).ToList(),
            due.Count >= 50);
    }
}

registry.AddCommands(commands => commands
    .RegisterFromAssembly(typeof(SendInvoiceReminderCommand).Assembly)
    .UsePostgreSqlRecurringLease(postgres =>
        postgres.UseConnectionString(configuration.GetConnectionString("OrdersDb")!))
    .AddRecurringBatch<DueInvoiceBatchSource>(host =>
    {
        host.Interval = TimeSpan.FromMinutes(5);
        host.RequireLease = true;
    }));
```

Return `RecurringCommandBatch.Empty` when there is nothing to send; an empty `Items` list sends nothing and the loop waits `Interval`. Returning `null` instead fails the pass, so prefer `Empty`. A batch whose `HasMoreWork` is true starts the next pass without waiting and reuses the held lease, unless the batch failed; see `AddRecurringBatch` for the busy-period contract.

The source scope, lease arbitration, and command registration requirements are documented on `AddRecurringBatch`. `LeaseName` defaults to the source type full name. See `RecurringCommandHostOptions.RequireLease` for the lease composition requirement and the duplicate-module rule.

## Sending One Command per Tenant

When each tenant needs its own pass of the same command, register a sweep with `AddRecurringForEachTenant`. Each pass enumerates `IRecurringTenantSource`, builds one command per tenant, and sends it with `ExecutionIdentity.ForTenant(tenant)`:

```csharp
public sealed class StoreTenantSource : IRecurringTenantSource
{
    private readonly TenantStore _store;

    public StoreTenantSource(TenantStore store)
    {
        _store = store;
    }

    public async IAsyncEnumerable<string> GetTenantsAsync(CancellationToken cancellationToken)
    {
        await foreach (var tenant in _store.ListIdsAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return tenant;
        }
    }
}

registry.AddCommands(commands => commands
    .RegisterFromAssembly(typeof(CloseStaleCartsCommand).Assembly)
    .AddRecurringForEachTenant<CloseStaleCartsCommand, StoreTenantSource>(
        tenant => new CloseStaleCartsCommand { TenantCode = tenant },
        host => host.Interval = TimeSpan.FromMinutes(5)));
```

The sweep accepts its source three ways, following the replaceable-port rule: no source, in which case each pass resolves `IRecurringTenantSource` from a fresh dispatch scope; a source instance; or a source type, registered as scoped and resolved from a fresh dispatch scope on every pass. A source resolved from a scope can take scoped dependencies such as a database context. The tenant identifiers are read before any command is sent. A sweep with no source instance, no source type, and no `IRecurringTenantSource` registration fails composition with `LiteBusConfigurationException`.

The sweep always sends one mediation per tenant, like a batch with `RecurringBatchIsolation.PerItem`. Failures stay isolated per tenant. One tenant's command failing or refused is logged and counted and never stops the rest of the sweep: a refused command is logged at Warning (EventId 2111) with the command type, the tenant, and the refusal code, a failure `IsConcurrencyConflict` classifies as a lost race is logged at Information (EventId 2104), and any other failure is logged at Error (EventId 2110) with the tenant. When any tenant failed or was refused, the sweep summary is logged at Warning (EventId 2112) and the pass waits `ErrorBackoff` instead of the interval. A blank tenant identifier is counted as failed and skipped. When no tenant failed, a command reporting more work through `IRecurringWorkReport` starts the next pass without waiting, as in the single-command loop.

## Aligning Passes to Wall-Clock Occurrences

By default the loop waits `Interval` between passes. Set `RecurringCommandHostOptions.Schedule` to align passes to wall-clock occurrences instead. The loop waits for the next occurrence before the first pass and between passes. A pass that reports more work still starts the next pass at once, a pass that fails still waits `ErrorBackoff`, and a schedule that reports no further occurrence ends the loop. `StartupDelay` still applies after host start, before the loop aligns to its first occurrence.

The built-in schedules live on the static `RecurringSchedules` type:

```csharp
registry.AddCommands(commands => commands
    .RegisterFromAssembly(typeof(SendDueRemindersCommand).Assembly)
    .AddRecurring(
        () => new SendDueRemindersCommand { BatchSize = 50 },
        host => host.Schedule = RecurringSchedules.DailyAt(new TimeOnly(9, 0), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"))));
```

| Schedule | Behavior |
| --- | --- |
| `RecurringSchedules.Every(interval)` | Fires once per interval after the current time. The interval must be positive. |
| `RecurringSchedules.DailyAt(time, zone)` | Fires once a day at a fixed local time. `zone` is `null` for UTC. |
| `RecurringSchedules.WeeklyOn(day, time, zone)` | Fires once a week on a fixed weekday at a fixed local time. `zone` is `null` for UTC. `day` must be a defined weekday. |

A local time inside a daylight-saving gap resolves forward to the next valid instant, and a local time inside a fall-back overlap resolves to the earlier occurrence. A scheduled loop still catches up nothing it missed while down: it waits for the next occurrence after the current time.

`IRecurringSchedule` is the replaceable port behind `Schedule`, with the single method `DateTimeOffset? GetNextOccurrence(DateTimeOffset after)`. An application with cron expressions implements it over its cron library and sets the implementation on `Schedule`. Implementations must be thread-safe and free of side effects, because the loop calls them on every wait.

## Failure Behavior

A pass that throws is logged, and the loop continues after `ErrorBackoff`, unless the `IsConcurrencyConflict` predicate classifies the exception as a lost race. A conflict is logged at Information (EventId 2104) and the loop waits `Interval`; see `RecurringCommandHostOptions.IsConcurrencyConflict` for the classification contract. The default predicate, `IsConcurrencyOrDatabaseConflict`, matches an optimistic-concurrency conflict type name or a database deadlock or serialization error anywhere in the inner or aggregate chain; see `RecurringCommandHostOptions.IsConcurrencyOrDatabaseConflict` for the exact SQLSTATE codes, error numbers, and SQLite result codes. `IsConcurrencyConflictByTypeName` keeps the type-name match alone. Set the predicate to `null` to treat conflicts as failures.

An exception never escapes the loop. A failing dependency is usually still failing one interval later, which is why the error delay is longer than the poll interval by default. A longer delay keeps a broken loop from filling the log at the polling rate.

Cancellation ends the loop immediately. The host's shutdown token is passed to the send, so an in-flight command observes it.

## Running on Several Replicas

By default the loop runs on every host instance; see `RecurringCommandHostOptions` for the concurrency boundary. A loop that must run on at most one instance at a time opts into single-flight with `RequireLease` and a registered `IRecurringCommandLease`:

```csharp
registry.AddCommands(commands => commands
    .RegisterFromAssembly(typeof(SendDueRemindersCommand).Assembly)
    .UsePostgreSqlRecurringLease(postgres =>
        postgres.UseConnectionString(configuration.GetConnectionString("OrdersDb")!))
    .AddRecurring(
        () => new SendDueRemindersCommand { BatchSize = 50 },
        host => host.RequireLease = true));
```

When `RequireLease` is true, each pass first tries the named lease; see `RecurringCommandHostOptions.RequireLease` for the acquisition and busy-period contract. A skipped pass is logged at Debug (EventId 2105) and the loop waits `Interval`.

The lease composition requirement and name default are documented on `RecurringCommandHostOptions.RequireLease` and `LeaseName`. There is no standing leader: every pass races for the lease, so the instance that runs varies from pass to pass. Loops sharing one explicit name single-flight against each other, which serializes two loops that must never overlap.

The lease can be lost mid-pass when another holder takes the row or renewals fail. The handle cancels its `LeaseLost` token, the loop ends the busy period, releases the handle, and waits `ErrorBackoff` instead of sending further passes. The in-flight send runs under a token linked to the loss signal, so a handler that observes cancellation stops with the pass. The loss is logged at Warning (EventId 2106).

`LiteBus.Commands.Recurring.Storage.PostgreSql` implements the lease as one renewable row per name in the `litebus_recurring_leases` table. Each acquisition is a single upsert that inserts the row or takes it over when its expiry has passed on the database clock, returning a row only to the winner. The holder identity recorded on the row is the machine name plus the process id plus a per-instance GUID, so operators can see which replica holds each lease.

```csharp
postgres.UseOptions(new PostgreSqlRecurringLeaseOptions
{
    SchemaName = "public",
    TableName = "litebus_recurring_leases",
    LeaseDuration = TimeSpan.FromSeconds(30),
    RenewInterval = TimeSpan.FromSeconds(10)
});
```

The handle renews its row in the background every `RenewInterval` (default one third of `LeaseDuration`, which defaults to 30 seconds) and deletes it on dispose when this holder still owns it. A holder whose process dies stops renewing, so its row becomes takeable once the duration lapses. That bounds how long a crashed replica blocks the loop. A renewal that fails or finds the lease taken stops renewing, cancels the handle's `LeaseLost` token so the pass holding it ends its busy period, and logs a warning (EventId 2110 when stolen, 2111 when the renewal itself fails). The row is left for its new holder or for expiry.

Set an explicit `RenewInterval` below `LeaseDuration`. A value at or above the duration fails validation with `ArgumentOutOfRangeException`, because the first renewal would land after the lease lapses. Size `LeaseDuration` from how quickly a replacement pass must run after a crash. A short duration frees a dead holder faster but narrows the margin a live holder has against a stalled renewal, while a long duration tolerates stalls but keeps a dead holder blocking longer. Every statement opens its own pooled connection and no connection is held between statements. The lease therefore works behind PgBouncer in transaction mode and with pooling disabled.

Operators inspect current holders with a plain query:

```sql
SELECT lease_name, holder, acquired_at, expires_at, renewed_at FROM litebus_recurring_leases ORDER BY lease_name;
```

The module creates the table at host startup by default and reports its shape through the `recurring.postgresql.schema` diagnostic probe. Disable startup creation with `DisableSchemaInitialization` when a migration pipeline owns the table.

There is a second pattern that needs no lease package and works with any store. Keep the recurring loop as a trigger that only writes a durable message, and put the exactly-once guarantee on the message the pass writes. A pass that enqueues one outbox event, or accepts one idempotent inbox command, can run on every replica. The outbox and inbox processors already lease and fence per message, so the work happens once however many loops wrote it. Prefer this shape when the work already flows through the inbox or the outbox, and reserve `RequireLease` for loops whose pass does its work inline.

## Limitations

By default a recurring command is a poll loop, not a scheduler; see `RecurringCommandHostOptions` for the scheduling boundary. Set `Schedule` when passes must align to wall-clock occurrences, as described in [Aligning Passes to Wall-Clock Occurrences](#aligning-passes-to-wall-clock-occurrences). There is still no missed-run catch-up for time the host was down, and no cron expression parser; an application with cron expressions implements `IRecurringSchedule` over its cron library.

The loop runs on every host instance by default; see `RecurringCommandHostOptions` for the concurrency boundary and [Running on Several Replicas](#running-on-several-replicas) for single-flight operations.

Make the handler safe for concurrent execution; see `RecurringCommandHostOptions` for the boundary. The usual safe shape claims rows with the database arbitrating:

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
