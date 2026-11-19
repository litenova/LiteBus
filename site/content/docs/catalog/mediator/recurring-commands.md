# Recurring Commands

- **ID**: `mediator.recurring-commands`
- **Summary**: Sends one command, one batch from a source (as one atomic batch by default, or one item at a time with `RecurringBatchIsolation.PerItem`), or one command per tenant, on a fixed interval or a wall-clock schedule for the life of the host, registered through `CommandModuleBuilder.AddRecurring`, `CommandModuleBuilder.AddRecurringBatch`, and `CommandModuleBuilder.AddRecurringForEachTenant`.
- **Guide**: [Recurring commands](../../integrations/recurring-commands.md)
- **Maturity**: GA

## What It Does

`AddRecurring` declares a child module of `CommandModule` that registers a `RecurringCommandBackgroundService<TCommand>` through the host manifest. Each pass opens its own dispatch scope, resolves `ICommandMediator`, and sends one command produced by the registered factory. The optional `identity` factory computes the `ExecutionIdentity` the pass sends for from the command it built, and the loop sends with `CommandMediationSettings { Identity = ... }`. Without it the mediation resolves the identity from the registered `IExecutionIdentityProvider` or falls back to `ExecutionIdentity.Anonymous`.

A pass that throws is logged, and the loop continues after `ErrorBackoff`. A pass that the `IsConcurrencyConflict` predicate classifies as an optimistic-concurrency conflict is logged at Information (EventId 2104) and waits the normal wait instead. An exception never escapes the loop. A result implementing `IRecurringWorkReport` with `HasMoreWork` set skips the wait so the next pass starts immediately.

With `RequireLease`, each pass first acquires the named `IRecurringCommandLease`. A pass that cannot acquire it is skipped at Debug (EventId 2105), and one acquired lease covers the whole busy period. When the held lease is lost mid-pass, the loop ends the busy period, releases the handle, and waits `ErrorBackoff`. The in-flight send is cancelled through a token linked to the loss signal.

`AddRecurringBatch` declares a child module of `CommandModule` that registers a `RecurringCommandBatchBackgroundService<TSource>` through the host manifest. Each pass opens a dispatch scope, resolves the source from it, calls `CreateBatchAsync` with the pass token (linked to `LeaseLost` when a lease is held), and disposes the scope. The pass then sends the returned batch according to `RecurringCommandBatch.Isolation`: `RecurringBatchIsolation.Atomic`, the default, sends the items as one all-or-nothing batch through `ICommandBatchMediator.TrySendManyAsync`, while `RecurringBatchIsolation.PerItem` sends the items one at a time, in list order, each with its own identity through `ICommandMediator.SendAsync`.

An empty `Items` list sends nothing and the loop waits the normal wait. An atomic batch runs its commands one at a time, in list order, inside one dispatch scope with one unit-of-work commit, and the first denied, invalid, faulted, or cancelled command rolls the whole batch back. Every item of an atomic batch must carry the same identity, or none; a batch that mixes identities fails the pass with `InvalidOperationException` naming `RecurringBatchIsolation.PerItem`. A refusal that rolls the batch back is logged once at Warning (EventId 2107) with the source type, the failing index, the failing command type, and the refusal code, and the pass waits `ErrorBackoff`. A per-item batch instead sends each item as its own mediation: one failing item is logged and counted and never stops the rest of the batch. A refused item is logged at Warning (EventId 2113) with the source type, the failing index, the failing command type, and the refusal code; an item whose exception `IsConcurrencyConflict` classifies as a lost race is logged at Information (EventId 2104); and any other failure is logged at Error (EventId 2108). When any item failed or was refused, the batch summary is logged at Warning (EventId 2109) and the pass waits `ErrorBackoff`. Otherwise the pass reports `HasMoreWork`: a batch reporting more work starts the next pass without waiting, reusing the held lease. A `null` batch fails the pass. Lease acquisition, lease loss, cancellation, and shutdown behave as in the single-command loop.

The source reads in its own scope, outside the sends that follow, so commands must still check the state they change. The lease, not a transaction, keeps two replicas from producing the same batch. Items cannot see each other's results, and a result-bearing command is sent without reading its result.

`AddRecurringForEachTenant` declares a child module of `CommandModule` that registers a `RecurringTenantSweepBackgroundService<TCommand>` through the host manifest. Each pass enumerates `IRecurringTenantSource`, builds one command per tenant, and sends it with `ExecutionIdentity.ForTenant(tenant)`. The tenant identifiers are read before any command is sent. The sweep always sends one mediation per tenant, like a batch with `RecurringBatchIsolation.PerItem`. Failures stay isolated per tenant: a refused command is logged at Warning (EventId 2111) with the command type, the tenant, and the refusal code; a failure `IsConcurrencyConflict` classifies as a lost race is logged at Information (EventId 2104); and any other failure, including a blank tenant identifier, is logged at Error (EventId 2110) with the tenant. When any tenant failed or was refused, the sweep summary is logged at Warning (EventId 2112) and the pass waits `ErrorBackoff`. When no tenant failed, a command reporting more work through `IRecurringWorkReport` starts the next pass without waiting. Lease acquisition, lease loss, cancellation, and shutdown behave as in the single-command loop.

When `RecurringCommandHostOptions.Schedule` is set, the loop waits for the next occurrence from `IRecurringSchedule.GetNextOccurrence` before the first pass and between passes, instead of waiting `Interval`. A pass that reports more work still starts the next pass at once, a pass that fails still waits `ErrorBackoff`, and a schedule that reports no further occurrence ends the loop. `StartupDelay` still applies after host start, before the loop aligns to its first occurrence.

## Public Surface

### Consumer Contracts

| Member | Purpose |
| --- | --- |
| `CommandModuleBuilder.AddRecurring<TCommand>(Func<TCommand>, Action<RecurringCommandHostOptions>?, Func<TCommand, ExecutionIdentity>?)` | Declares a loop with an explicit command factory and an optional per-pass identity |
| `CommandModuleBuilder.AddRecurring<TCommand>(Action<RecurringCommandHostOptions>?, Func<TCommand, ExecutionIdentity>?)` | Declares a loop for a command with a parameterless constructor and an optional per-pass identity |
| `CommandModuleBuilder.AddRecurringBatch<TSource>(Action<RecurringCommandHostOptions>?)` | Declares a loop that sends one batch per pass from the source, as one atomic batch by default or one item at a time with `RecurringBatchIsolation.PerItem` |
| `CommandModuleBuilder.AddRecurringForEachTenant<TCommand>(Func<string, TCommand>, Action<RecurringCommandHostOptions>?)` | Declares a sweep that sends one command per tenant, resolving the tenant source from a dispatch scope |
| `CommandModuleBuilder.AddRecurringForEachTenant<TCommand>(Func<string, TCommand>, IRecurringTenantSource, Action<RecurringCommandHostOptions>?)` | Declares a sweep with a tenant source instance |
| `CommandModuleBuilder.AddRecurringForEachTenant<TCommand, TTenantSource>(Func<string, TCommand>, Action<RecurringCommandHostOptions>?)` | Declares a sweep with a tenant source type, registered as scoped |
| `IRecurringTenantSource.GetTenantsAsync(CancellationToken)` | Lists the tenant identifiers one sweep pass sends for |
| `IRecurringSchedule.GetNextOccurrence(DateTimeOffset)` | Returns the next occurrence strictly after the supplied instant, or `null` when the schedule ends |
| `RecurringSchedules.Every(TimeSpan)` | A schedule that fires once per interval after the current time |
| `RecurringSchedules.DailyAt(TimeOnly, TimeZoneInfo?)` | A schedule that fires once a day at a fixed local time, UTC when the zone is `null` |
| `RecurringSchedules.WeeklyOn(DayOfWeek, TimeOnly, TimeZoneInfo?)` | A schedule that fires once a week on a fixed weekday at a fixed local time, UTC when the zone is `null` |
| `IRecurringCommandBatchSource.CreateBatchAsync(CancellationToken)` | Produces the batch one pass sends |
| `RecurringCommandBatch` | The items one pass sends: `Items`, `HasMoreWork`, `Isolation` (default `Atomic`), `Empty`, and `From` overloads for plain commands and items |
| `RecurringBatchIsolation` | `Atomic` (default): the pass sends the batch as one all-or-nothing batch through `ICommandBatchMediator.TrySendManyAsync`; `PerItem`: the pass sends each item as its own mediation with its own identity |
| `RecurringCommandItem(ICommand, ExecutionIdentity?)` | One batch command with the identity its pass sends it for, or `null` for no identity |
| `IRecurringWorkReport.HasMoreWork` | Optional result contract that skips the wait when work remains |
| `RecurringCommandHostOptions` | `Enabled`, `Interval`, `Schedule` (default `null`, meaning `Interval`), `StartupDelay`, `ErrorBackoff`, `IsConcurrencyConflict` (default `IsConcurrencyOrDatabaseConflict`; when `null`, conflicts are treated as failures), `RequireLease`, `LeaseName` (default command type full name, or source type full name for batch loops) |
| `IRecurringCommandLease.TryAcquireAsync` | Named single-flight acquisition. Returns a releasing handle that signals loss, or `null` when the lease is held |
| `IRecurringCommandLeaseHandle.LeaseLost` | Token cancelled when the held lease is taken by another holder or renewals fail |
| `CommandModuleBuilder.UsePostgreSqlRecurringLease` | Registers the renewable PostgreSQL lease-row single-flight from `LiteBus.Commands.Recurring.Storage.PostgreSql` |
| `PostgreSqlRecurringLeaseOptions` | The leases table schema and name, `LeaseDuration` (default 30 seconds), and `RenewInterval` (default null, meaning one third of the duration, and an explicit value must stay below the duration) |
| `RecurringCommandDeclaration<TCommand>` | The registered factory, options, and optional identity factory, resolved by the loop |
| `RecurringTenantSweepDeclaration<TCommand>` | The registered tenant factory, tenant source, and options, resolved by the sweep |

### Registration

- Composed inside `AddCommands`, so a loop cannot be registered without the axis that mediates it. `CommandModuleBuilder.RegisterChildModule(IModule)` is the seam feature packages compose through, and `LiteBus.Commands.Recurring` is the worked example.
- Registers `RecurringCommandBackgroundService<TCommand>` through `IModuleConfiguration.RegisterBackgroundService`.
- One registration per command type. Each gets its own loop and options.
- `AddRecurringBatch` registers `TSource` as a scoped service and a `RecurringCommandBatchModule<TSource>` child through `RegisterChildModule`. The module registers `RecurringCommandBatchBackgroundService<TSource>` through `IModuleConfiguration.RegisterBackgroundService`.
- One registration per source type. Each gets its own loop and options. Registering the same source type twice fails composition with `LiteBusConfigurationException` through the registry duplicate-module rule.
- `AddRecurringForEachTenant` registers `TCommand` with the command module and a `RecurringTenantSweepModule<TCommand>` child through `RegisterChildModule`. The module registers `RecurringTenantSweepBackgroundService<TCommand>` through `IModuleConfiguration.RegisterBackgroundService`. The type overload registers the tenant source type as scoped.
- A sweep with no source instance, no source type, and no `IRecurringTenantSource` registration fails composition with `LiteBusConfigurationException` through a deferred composition validation.
- A loop with `RequireLease` registers a composition validation that fails the build with `LiteBusConfigurationException` when no `IRecurringCommandLease` is registered. Lease implementations register through `RegisterChildModule` so composition can see them. A batch loop applies the same deferred lease check, with a default lease name of the source type full name. A sweep applies the same check, with a default lease name of the command type full name.

### Configuration

- Timing values are validated when the module builds, so a zero or negative interval or error backoff fails at boot. The loop snapshots the validated values when it starts, so post-start option mutation does not change the pacing of a running loop.
- With `Enabled = false`, the loop logs once and never sends.
- `IsConcurrencyConflict` defaults to `IsConcurrencyOrDatabaseConflict`. It matches a type name ending in `ConcurrencyException`, or a database deadlock or serialization failure (SQLSTATE `40001` or `40P01`, error number 1205 or 1213, or SQLite busy or locked), anywhere in the inner or aggregate chain. `IsConcurrencyConflictByTypeName` keeps the type-name match alone.
- `RequireLease = true` makes each pass acquire the named lease first. A pass that cannot acquire it is skipped at Debug and the loop waits `Interval`.
- `LeaseName` defaults to the command type's full name, or the source type's full name for batch loops. Loops sharing a name single-flight against each other.
- Batch loops use the same `RecurringCommandHostOptions` timing values, validation, snapshotting, and `Enabled` behavior as single-command loops. Sweep loops do too.
- `Schedule` defaults to `null`, which waits `Interval` between passes. When set, the loop waits for the next occurrence before the first pass and between passes; a pass that reports more work still starts the next pass at once, a pass that fails still waits `ErrorBackoff`, and a schedule with no further occurrence ends the loop. The loop snapshots the schedule reference when it starts.
- `DailyAt` and `WeeklyOn` read the local time in the supplied zone, or UTC when it is `null`. A local time inside a daylight-saving gap resolves forward to the next valid instant, and a local time inside a fall-back overlap resolves to the earlier occurrence. `Every` rejects a zero or negative interval with `ArgumentOutOfRangeException`, and `WeeklyOn` rejects an undefined weekday the same way.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Commands.Recurring` | Feature bridge from the command axis to the runtime background-service manifest |
| `LiteBus.Commands.Recurring.Storage.PostgreSql` | Feature bridge implementing the recurring lease with a renewable PostgreSQL lease row |

## Requires

- `mediator.commands` for the pipeline each pass runs through
- A host that runs `IBackgroundService` registrations, which the generic host bridging provides

## Invariants

- The command factory is called once per pass, not once at registration.
- A pass failure never ends the loop.
- Cancellation of the host token ends the loop without waiting out the interval.
- By default the loop runs on every host instance. With `RequireLease`, each pass runs on at most one instance, and one lease covers the busy period.
- A lost lease ends the busy period. The loop releases the handle, waits `ErrorBackoff`, and cancels the in-flight send through a linked token.
- The batch source is resolved from a fresh scope on each pass, and the scope is disposed after the read.
- An empty batch sends nothing and the loop waits the normal wait. An atomic batch commits or rolls back as one; a refusal that rolls it back waits `ErrorBackoff`. In a per-item batch, one failing item never stops the rest of the batch. A per-item batch with any failure waits `ErrorBackoff`. A batch with no failure that reports more work starts the next pass without waiting.
- Commands returned by a source must be registered with the command module.
- A sweep sends one command per tenant with `ExecutionIdentity.ForTenant(tenant)`. The tenant identifiers are read before any command is sent. The sweep always sends one mediation per tenant, like a batch with `RecurringBatchIsolation.PerItem`. One failing tenant never stops the rest of the sweep.
- A scheduled loop waits for the next occurrence before the first pass, not only between passes. A schedule with no further occurrence ends the loop.
- `IRecurringSchedule` implementations must be thread-safe and free of side effects.

## Non-Goals

- Cron expression parsing. An application with cron expressions implements `IRecurringSchedule` over its cron library.
- Missed-run catch-up for time the host was down. A scheduled loop waits for the next occurrence after the current time.
- Standing leader election across a deployment. Single-flight is per pass through an `IRecurringCommandLease`, not a stable leader. Work that must be claimed exactly once can also use the durable axes, which lease and fence per message.

## Observability

| Event | Level | Meaning |
| --- | --- | --- |
| `2101` `LoopStarted` | Information | The loop started, with its interval |
| `2102` `LoopDisabled` | Information | `Enabled` was false, so the loop will not run |
| `2103` `PassFailed` | Error | A send threw, so the loop continues after `ErrorBackoff` |
| `2104` `PassConflicted` | Information | A send lost a concurrency race, so the loop continues at `Interval` |
| `2105` `LeaseSkipped` | Debug | The lease was held elsewhere, so the pass was skipped, and the loop continues at `Interval` |
| `2106` `LeaseLost` | Warning | The held lease was taken or renewals failed, so the busy period ends and the loop backs off |
| `2107` `BatchRefused` | Warning | The atomic batch was refused and rolled back, so the loop continues after `ErrorBackoff` |
| `2108` `BatchItemFailed` | Error | One per-item batch item failed, so it was counted and the batch continues with the next item |
| `2109` `BatchCompletedWithFailures` | Warning | A per-item batch finished with failures, so the loop continues after `ErrorBackoff` |
| `2110` `TenantCommandFailed` | Error | One tenant's command failed, so it was counted and the sweep continues with the next tenant |
| `2111` `TenantCommandRefused` | Warning | One tenant's command was refused, so it was counted and the sweep continues with the next tenant |
| `2112` `SweepCompletedWithFailures` | Warning | A sweep finished with failures, so the loop continues after `ErrorBackoff` |
| `2113` `BatchItemRefused` | Warning | One per-item batch item was refused, so it was counted and the batch continues with the next item |
| `2110` `LeaseRenewalStolen` | Warning | The lease was taken by another holder, so renewal stopped and the row was left untouched |
| `2111` `LeaseRenewalFailed` | Warning | A lease renewal failed, so renewal stopped and the lease expires on the database clock |

Event numbers 2110 and 2111 appear twice because two loggers use them: the recurring loop logger writes `TenantCommandFailed` and `TenantCommandRefused`, and the PostgreSQL lease package logger writes `LeaseRenewalStolen` and `LeaseRenewalFailed`. The category name tells them apart.

## References

- [Recurring Commands](../../integrations/recurring-commands.md)

## Test Coverage

### Covered Use Cases

| Test | Project |
| --- | --- |
| `RecurringCommandHostTests.Loop_sends_the_command_repeatedly` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandHostTests.Loop_survives_a_failing_pass_and_keeps_sending` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandHostTests.A_disabled_loop_never_sends` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandHostTests.A_result_reporting_more_work_sends_again_without_waiting` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandHostTests.The_loop_is_registered_as_a_background_service_on_the_host_manifest` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandHostTests.A_negative_interval_is_refused_at_composition` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandHostOptionsValidationTests.A_zero_interval_is_refused_at_composition` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandHostOptionsValidationTests.A_zero_error_backoff_is_refused_at_composition` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandLeaseTests.Pass_is_skipped_when_the_lease_is_not_acquired` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandLeaseTests.Pass_runs_and_releases_the_lease_when_acquired` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandLeaseTests.Lease_is_released_even_when_the_send_throws` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandLeaseTests.Lease_release_failure_does_not_end_the_loop` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandLeaseTests.One_lease_covers_the_busy_period` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandLeaseTests.Lease_loss_stops_the_busy_period_releases_and_backs_off` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandLeaseTests.Lease_loss_cancels_the_in_flight_send` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandLeaseTests.Shutdown_with_a_held_lease_and_failing_release_completes_cleanly` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandHostTests.Mutating_options_after_start_does_not_change_loop_pacing` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandLeaseTests.Composition_fails_when_require_lease_has_no_lease` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandLeaseTests.Lease_name_defaults_to_the_command_type_full_name` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandLeaseTests.Custom_lease_name_is_used_when_configured` | `LiteBus.Mediator.UnitTests` |
| `PostgreSqlRecurringCommandLeaseTests.TryAcquireAsync_WhenNameIsHeld_ShouldReturnNullUntilReleased` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.TryAcquireAsync_WhenNamesDiffer_ShouldGrantBoth` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.TryAcquireAsync_WhenTwoHoldersRace_ShouldGrantExactlyOne` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.TryAcquireAsync_AfterExpiryWithoutRenewal_ShouldGrantLeaseToAnotherHolder` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.TryAcquireAsync_WhenSameHolderReacquires_ShouldReturnNull` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.LeaseOptions_WhenRenewIntervalIsNotBelowDuration_ShouldThrow` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.SchemaInitializer_ShouldResolveTheDataSourceOwnerSoShutdownDisposesIt` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.LeaseLost_AfterLeaseIsStolen_ShouldCancel` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.LeaseLost_WhenRenewalFails_ShouldCancel` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.TryAcquireAsync_WithRenewal_ShouldStayHeldPastOriginalExpiry` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.Renewal_AfterLeaseIsStolen_ShouldStopAndWarnOnce` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.Renewal_WhenTableIsDropped_ShouldStopWithoutResurrectingTheRow` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.TryAcquireAsync_WithUnpooledAndPooledSources_ShouldCoordinate` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.EnsureAsync_ShouldCreateSchemaAndBeIdempotent` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.ValidateAsync_ShouldThrowDriftWhenTableIsMissing` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.DiagnosticCheck_ShouldReportHealthyAfterSchemaIsCreated` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.DiagnosticCheck_ShouldReportUnhealthyWhenTableIsMissing` | `LiteBus.Storage.IntegrationTests` |
| `PostgreSqlRecurringCommandLeaseTests.SchemaInitializer_ShouldCreateSchemaSoTheLeaseCanAcquire` | `LiteBus.Storage.IntegrationTests` |
| `RecurringCommandIdentityTests.Loop_sends_with_the_identity_the_registration_supplies` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandIdentityTests.Loop_without_identity_sends_anonymously` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandIdentityTests.Result_bearing_command_sends_with_the_identity_the_registration_supplies` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandBatchItemTests.From_plain_commands_assigns_no_identity` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandBatchItemTests.From_items_preserves_commands_and_identities` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandBatchItemTests.Empty_has_no_items` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandBatchItemTests.Each_item_sends_with_its_own_identity` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandBatchItemTests.One_failing_item_does_not_stop_the_rest` | `LiteBus.Mediator.UnitTests` |
| `RecurringCommandBatchItemTests.Refused_items_are_isolated_logged_and_back_the_loop_off` | `LiteBus.Mediator.UnitTests` |
| `RecurringTenantSweepTests.Sweep_sends_one_command_per_tenant_with_the_tenant_identity` | `LiteBus.Mediator.UnitTests` |
| `RecurringTenantSweepTests.One_failing_tenant_does_not_stop_the_sweep` | `LiteBus.Mediator.UnitTests` |
| `RecurringTenantSweepTests.A_blank_tenant_is_counted_and_skipped` | `LiteBus.Mediator.UnitTests` |
| `RecurringTenantSweepTests.Refused_tenant_commands_are_isolated_and_logged` | `LiteBus.Mediator.UnitTests` |
| `RecurringTenantSweepTests.Type_source_is_resolved_from_a_dispatch_scope_on_each_pass` | `LiteBus.Mediator.UnitTests` |
| `RecurringTenantSweepTests.Registry_source_is_used_when_no_source_is_passed` | `LiteBus.Mediator.UnitTests` |
| `RecurringTenantSweepTests.Sweep_without_tenant_source_fails_composition` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.Every_returns_the_instant_one_interval_later` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.Every_rejects_a_non_positive_interval` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.DailyAt_returns_today_when_the_time_is_ahead` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.DailyAt_returns_tomorrow_when_the_time_has_passed` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.DailyAt_returns_tomorrow_when_the_time_is_now` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.DailyAt_without_zone_reads_the_time_as_utc` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.WeeklyOn_returns_the_same_day_when_the_weekday_and_time_are_ahead` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.WeeklyOn_returns_next_week_when_the_time_has_passed` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.WeeklyOn_returns_the_coming_weekday` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.WeeklyOn_rejects_an_undefined_weekday` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.DailyAt_resolves_a_daylight_saving_gap_to_the_next_valid_instant` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.DailyAt_resolves_an_ambiguous_time_to_the_earlier_occurrence` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.WeeklyOn_resolves_a_daylight_saving_gap_to_the_next_valid_instant` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.Scheduled_loop_waits_for_the_first_occurrence_then_sends` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.Scheduled_loop_waits_for_the_next_occurrence_between_passes` | `LiteBus.Mediator.UnitTests` |
| `RecurringScheduleTests.Schedule_without_further_occurrence_ends_the_loop_without_sending` | `LiteBus.Mediator.UnitTests` |

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Loop under a real generic host start and stop | Yes | Covered indirectly through the manifest assertion only | Integration | Low |
| Two instances sending the same recurring command | Yes, by design | No test asserts the concurrent shape | Integration | Low |
