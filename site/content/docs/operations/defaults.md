# Processor, Retry, Dispatch, and Cleanup Defaults

Inbox and outbox processors, retry policies, immediate dispatch, and retention cleanup each read their behavior from an options type. This page lists every property of those types, its default, and what changes when you set it, so sizing and upgrade decisions have one record to check. It assumes you register the processors from [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md).

A unit test in `tests/LiteBus.Extensions.UnitTests/Documentation` reads every table on this page and compares each default with a new instance of the matching options type, so the tables cannot drift from source.

## `InboxProcessorOptions`

Namespace `LiteBus.Inbox.Abstractions`. Set through `inbox.UseProcessorOptions(...)`. The processor reads these values at creation.

| Property | Default | Effect of changing it |
| --- | --- | --- |
| `BatchSize` | `20` | A larger batch leases more rows per pass, which reduces store round trips but holds more leases during dispatch and widens the effect of a worker that stops mid-pass. Must be greater than zero, or processor construction throws. |
| `LeaseDuration` | `00:01:00` (1 minute) | A longer lease tolerates slow handlers but delays reclaim of rows whose worker crashed without releasing them. `LeaseHeartbeatInterval` must stay at or below half of this value. |
| `Retry` | `new RetryOptions()` | Replaces the retry policy applied after dispatch failures. See [`RetryOptions`](#retryoptions). |
| `LeaseOwner` | `null` (generated owner) | An explicit owner names the instance on leased rows for operations tooling. When omitted, the processor builds one from machine name, process id, and a random suffix, and still appends a session suffix that fences stale sessions. |
| `DispatcherConcurrency` | `1` | More workers dispatch one pass in parallel, which raises throughput but makes arrival order non-deterministic. Keep `1` when downstream consumers are sensitive to order; see [Ordering and Concurrency Guarantees](../reliable-messaging/ordering.md). Must be greater than zero. |
| `LeaseHeartbeatInterval` | `00:00:15` (15 seconds) | A shorter interval renews leases more often at the cost of more store writes. `00:00:00` disables renewal, so a dispatch slower than the lease loses it. Must stay at or below half of `LeaseDuration`. |
| `TenantId` | `null` (all tenants) | Set to lease only that tenant partition, so one processor instance serves one tenant. |
| `HonorShutdownTokenOnPersist` | `false` | `true` lets shutdown cancel terminal persist calls, which stops faster but risks a duplicate execution when the persist never lands. `false` delays shutdown instead. |
| `HookFailurePolicy` | `null` (dispatcher recommendation) | An explicit value overrides the dispatcher recommendation when an after-dispatch hook throws after dispatch succeeded. With `null`, in-process inbox dispatch moves the message to dead letter. |

## `OutboxProcessorOptions`

Namespace `LiteBus.Outbox.Abstractions`. Set through `outbox.UseProcessorOptions(...)`. The processor reads these values at creation.

| Property | Default | Effect of changing it |
| --- | --- | --- |
| `BatchSize` | `20` | A larger batch leases more rows per pass, which reduces store round trips but holds more leases while dispatchers publish. Must be greater than zero, or processor construction throws. |
| `LeaseDuration` | `00:01:00` (1 minute) | A longer lease tolerates slow brokers but delays reclaim of rows whose publisher crashed without releasing them. `LeaseHeartbeatInterval` must stay at or below half of this value. |
| `Retry` | `new RetryOptions()` | Replaces the retry policy applied after dispatch failures. See [`RetryOptions`](#retryoptions). |
| `LeaseOwner` | `null` (generated owner) | An explicit owner names the instance on leased rows for operations tooling. When omitted, the processor builds one from machine name, process id, and a random suffix, and still appends a session suffix that fences stale sessions. |
| `DispatcherConcurrency` | `1` | More workers dispatch one pass in parallel, which raises throughput but makes arrival order non-deterministic. Keep `1` when downstream consumers are sensitive to order; see [Ordering and Concurrency Guarantees](../reliable-messaging/ordering.md). Must be greater than zero. |
| `LeaseHeartbeatInterval` | `00:00:15` (15 seconds) | A shorter interval renews leases more often at the cost of more store writes. `00:00:00` disables renewal, so a dispatch slower than the lease loses it. Must stay at or below half of `LeaseDuration`. |
| `TenantId` | `null` (all tenants) | Set to lease only that tenant partition, so one processor instance serves one tenant. |
| `HonorShutdownTokenOnPersist` | `false` | `true` lets shutdown cancel terminal persist calls, which stops faster but risks a duplicate publication when the persist never lands. `false` delays shutdown instead. |
| `HookFailurePolicy` | `null` (dispatcher recommendation) | An explicit value overrides the dispatcher recommendation when an after-dispatch hook throws after dispatch succeeded. With `null`, transport dispatchers complete despite the hook failure so a message the broker already accepted is not published twice, while in-process dispatch moves the message to dead letter. |
| `MaxLeasedPerTenant` | `null` (no cap) | Set to return at most that many envelopes of one tenant per lease call, so a busy tenant cannot fill every batch. Unscoped envelopes count as one tenant. |

## `InboxProcessorHostOptions`

Namespace `LiteBus.Inbox.Abstractions`. Set through `inbox.EnableInboxProcessor(host => ...)`. These values control the background loop lifecycle and polling only.

| Property | Default | Effect of changing it |
| --- | --- | --- |
| `Enabled` | `true` | `false` stops the loop from running, which parks the axis without removing its registration. |
| `PollInterval` | `00:00:01` (1 second) | A longer delay cuts idle store load but slows pickup of newly due rows. It applies when the previous batch was empty or partial, and is skipped after a full batch while `UseAdaptivePolling` is `true`. Must not be negative. |
| `StartupDelay` | `00:00:00` (none) | Delays the first poll after host start, so dependencies can warm up before the loop claims work. Must not be negative. |
| `UseAdaptivePolling` | `true` | `false` always waits `PollInterval` between passes, even after a full batch, which steadies store load but drains backlogs more slowly. |

## `OutboxProcessorHostOptions`

Namespace `LiteBus.Outbox.Abstractions`. Set through `outbox.EnableOutboxProcessor(host => ...)`. These values control the background loop lifecycle and polling only.

| Property | Default | Effect of changing it |
| --- | --- | --- |
| `Enabled` | `true` | `false` stops the loop from running, which parks the axis without removing its registration. |
| `PollInterval` | `00:00:01` (1 second) | A longer delay cuts idle store load but slows pickup of newly due rows. It applies when the previous batch was empty or partial, and is skipped after a full batch while `UseAdaptivePolling` is `true`. Must not be negative. |
| `StartupDelay` | `00:00:00` (none) | Delays the first poll after host start, so dependencies can warm up before the loop claims work. Must not be negative. |
| `UseAdaptivePolling` | `true` | `false` always waits `PollInterval` between passes, even after a full batch, which steadies store load but drains backlogs more slowly. |

## `RetryOptions`

Namespace `LiteBus.Messaging.Abstractions`. Set as the `Retry` property of the processor options. Stores receive only the computed next-visible timestamp, so the policy stays outside the storage layer.

| Property | Default | Effect of changing it |
| --- | --- | --- |
| `MaxAttempts` | `5` | More attempts tolerate longer outages before the row moves to dead letter; each extra attempt adds one more delay step. Must be greater than zero. |
| `InitialDelay` | `00:00:05` (5 seconds) | Sets the delay before the first retry and the base of the exponential curve. Must not be negative. |
| `MaxDelay` | `00:05:00` (5 minutes) | Caps the calculated delay for `Fixed` and `Exponential` backoff. It does not clamp entries of an explicit `Schedule` ladder. Must not be negative. |
| `Backoff` | `Exponential` | `Fixed` reuses `InitialDelay` for every retry; `Schedule` takes each delay from `Delays`, which must then be non-empty with no negative entries. |
| `Delays` | `empty` | The explicit ladder used only when `Backoff` is `Schedule`, one delay per retry in order. An attempt past the end repeats the last entry, and jitter still applies. Ignored for every other backoff. |
| `UseJitter` | `true` | `false` makes delays exact, which helps tests but lets many messages retry at the same instant under concurrent load. `true` multiplies each delay by a random factor between 0.8 and 1.2. |
| `IsPermanentFailure` | `null` (retry every failure the built-in set allows) | Set a predicate to dead-letter a message on the first failure it recognizes, regardless of `MaxAttempts`. The built-in non-retryable set (missing handler, multiple handlers, guard denial, validation failure) applies either way. |

With the defaults, the four retry delays before jitter are 5, 10, 20, and 40 seconds: each step doubles `InitialDelay`, and the 5 minute cap never binds. Jitter then multiplies each delay by a factor between 0.8 and 1.2, staying within `MaxDelay`.

## `OutboxImmediateDispatchOptions`

Namespace `LiteBus.Outbox.Abstractions`. Set through `outbox.EnableImmediateDispatch(options => ...)`. The values are read once at composition time. See [Immediate Dispatch](../reliable-messaging/immediate-dispatch.md).

| Property | Default | Effect of changing it |
| --- | --- | --- |
| `Timeout` | `00:00:10` (10 seconds) | Bounds the post-commit wait in `Await` mode. A shorter timeout returns to the caller sooner but releases more envelopes to the processor as timed out; a longer timeout holds the caller while dispatch works. Must be positive. |
| `FallbackDelay` | `00:00:30` (30 seconds) | How long a staged immediate envelope stays invisible to the processor. Must be greater than `Timeout`: a shorter delay risks the processor racing the dispatch, while a longer delay slows recovery when the process stops before dispatching. |
| `Mode` | `Await` | `Background` runs dispatch on a background task after the commit and returns without waiting, so the caller stays fast while handlers still usually run ahead of the processor poll. |

## `InboxCleanupHostOptions`

Namespace `LiteBus.Inbox.Abstractions`. Set through `inbox.EnableCleanup(cleanup => ...)`. The loop deletes completed rows older than the retention period.

| Property | Default | Effect of changing it |
| --- | --- | --- |
| `Enabled` | `true` | `false` disables the cleanup loop, so completed rows accumulate until the loop runs again or `IInboxManager.RunRetentionPurgeAsync` runs on demand. |
| `Interval` | `01:00:00` (1 hour) | Sets the delay between cleanup passes. Must be greater than zero. |
| `Retention` | `null` (no deletion) | Set to delete completed rows older than this period. While `null`, the loop exits without deleting rows. Must be greater than zero when set. |

## `OutboxCleanupHostOptions`

Namespace `LiteBus.Outbox.Abstractions`. Set through `outbox.EnableCleanup(cleanup => ...)`. The loop deletes published rows older than the retention period.

| Property | Default | Effect of changing it |
| --- | --- | --- |
| `Enabled` | `true` | `false` disables the cleanup loop, so published rows accumulate until the loop runs again or `IOutboxManager.RunRetentionPurgeAsync` runs on demand. |
| `Interval` | `01:00:00` (1 hour) | Sets the delay between cleanup passes. Must be greater than zero. |
| `Retention` | `null` (no deletion) | Set to delete published rows older than this period. While `null`, the loop exits without deleting rows, and `IOutboxManager.RunRetentionPurgeAsync` throws `OutboxManagementException`. Must be greater than zero when set. |

## Next

- [Inbox](../reliable-messaging/inbox.md) and [Outbox](../reliable-messaging/outbox.md) processor options
- [Immediate Dispatch](../reliable-messaging/immediate-dispatch.md)
- [Production Runbook](runbook.md)
