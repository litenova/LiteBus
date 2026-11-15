# Recurring Commands

- **ID**: `mediator.recurring-commands`
- **Summary**: Sends one command, or one batch from a source, on a fixed interval for the life of the host, registered through `CommandModuleBuilder.AddRecurring` and `CommandModuleBuilder.AddRecurringBatch`.
- **Guide**: [Recurring commands](../../integrations/recurring-commands.md)
- **Maturity**: GA

## What It Does

`AddRecurring` declares a child module of `CommandModule` that registers a `RecurringCommandBackgroundService<TCommand>` through the host manifest. Each pass opens its own dispatch scope, resolves `ICommandMediator`, and sends one command produced by the registered factory.

A pass that throws is logged, and the loop continues after `ErrorBackoff`. A pass that the `IsConcurrencyConflict` predicate classifies as an optimistic-concurrency conflict is logged at Information (EventId 2104) and waits `Interval` instead. An exception never escapes the loop. A result implementing `IRecurringWorkReport` with `HasMoreWork` set skips the interval so the next pass starts immediately.

With `RequireLease`, each pass first acquires the named `IRecurringCommandLease`. A pass that cannot acquire it is skipped at Debug (EventId 2105), and one acquired lease covers the whole busy period. When the held lease is lost mid-pass, the loop ends the busy period, releases the handle, and waits `ErrorBackoff`. The in-flight send is cancelled through a token linked to the loss signal.

`AddRecurringBatch` declares a child module of `CommandModule` that registers a `RecurringCommandBatchBackgroundService<TSource>` through the host manifest. Each pass opens a dispatch scope, resolves the source from it, calls `CreateBatchAsync` with the pass token (linked to `LeaseLost` when a lease is held), and disposes the scope. The pass then sends the returned commands as one batch through `ICommandBatchMediator.TrySendManyAsync`.

An empty `Commands` list sends nothing and the loop waits `Interval`. A committed batch whose `HasMoreWork` is true starts the next pass without waiting, reusing the held lease. A batch that a refusal rolls back is logged at Warning (EventId 2107) with the source type, the failing index, the failing command type, and the refusal code, and the loop waits `ErrorBackoff`. A fault or a failed commit goes through the same paths as the single-command loop: a conflict classified by `IsConcurrencyConflict` is logged at Information (EventId 2104) and waits `Interval`, and anything else logs `PassFailed` and waits `ErrorBackoff`. Lease acquisition, lease loss, cancellation, and shutdown behave as in the single-command loop.

The source reads in its own scope, outside the batch transaction, so commands must still check the state they change. The lease, not the transaction, keeps two replicas from producing the same batch.

## Public Surface

### Consumer Contracts

| Member | Purpose |
| --- | --- |
| `CommandModuleBuilder.AddRecurring<TCommand>(Func<TCommand>, Action<RecurringCommandHostOptions>?)` | Declares a loop with an explicit command factory |
| `CommandModuleBuilder.AddRecurring<TCommand>(Action<RecurringCommandHostOptions>?)` | Declares a loop for a command with a parameterless constructor |
| `CommandModuleBuilder.AddRecurringBatch<TSource>(Action<RecurringCommandHostOptions>?)` | Declares a loop that sends one batch per pass from the source |
| `IRecurringCommandBatchSource.CreateBatchAsync(CancellationToken)` | Produces the commands one pass sends as a single batch |
| `RecurringCommandBatch` | The commands one pass sends: `Commands`, `HasMoreWork`, `Empty`, and `From` |
| `IRecurringWorkReport.HasMoreWork` | Optional result contract that skips the interval when work remains |
| `RecurringCommandHostOptions` | `Enabled`, `Interval`, `StartupDelay`, `ErrorBackoff`, `IsConcurrencyConflict` (default `IsConcurrencyOrDatabaseConflict`; when `null`, conflicts are treated as failures), `RequireLease`, `LeaseName` (default command type full name, or source type full name for batch loops) |
| `IRecurringCommandLease.TryAcquireAsync` | Named single-flight acquisition. Returns a releasing handle that signals loss, or `null` when the lease is held |
| `IRecurringCommandLeaseHandle.LeaseLost` | Token cancelled when the held lease is taken by another holder or renewals fail |
| `CommandModuleBuilder.UsePostgreSqlRecurringLease` | Registers the renewable PostgreSQL lease-row single-flight from `LiteBus.Commands.Recurring.Storage.PostgreSql` |
| `PostgreSqlRecurringLeaseOptions` | The leases table schema and name, `LeaseDuration` (default 30 seconds), and `RenewInterval` (default null, meaning one third of the duration, and an explicit value must stay below the duration) |
| `RecurringCommandDeclaration<TCommand>` | The registered factory and options, resolved by the loop |

### Registration

- Composed inside `AddCommands`, so a loop cannot be registered without the axis that mediates it. `CommandModuleBuilder.RegisterChildModule(IModule)` is the seam feature packages compose through, and `LiteBus.Commands.Recurring` is the worked example.
- Registers `RecurringCommandBackgroundService<TCommand>` through `IModuleConfiguration.RegisterBackgroundService`.
- One registration per command type. Each gets its own loop and options.
- `AddRecurringBatch` registers `TSource` as a scoped service and a `RecurringCommandBatchModule<TSource>` child through `RegisterChildModule`. The module registers `RecurringCommandBatchBackgroundService<TSource>` through `IModuleConfiguration.RegisterBackgroundService`.
- One registration per source type. Each gets its own loop and options. Registering the same source type twice fails composition with `LiteBusConfigurationException` through the registry duplicate-module rule.
- A loop with `RequireLease` registers a composition validation that fails the build with `LiteBusConfigurationException` when no `IRecurringCommandLease` is registered. Lease implementations register through `RegisterChildModule` so composition can see them. A batch loop applies the same deferred lease check, with a default lease name of the source type full name.

### Configuration

- Timing values are validated when the module builds, so a zero or negative interval or error backoff fails at boot. The loop snapshots the validated values when it starts, so post-start option mutation does not change the pacing of a running loop.
- With `Enabled = false`, the loop logs once and never sends.
- `IsConcurrencyConflict` defaults to `IsConcurrencyOrDatabaseConflict`. It matches a type name ending in `ConcurrencyException`, or a database deadlock or serialization failure (SQLSTATE `40001` or `40P01`, error number 1205 or 1213, or SQLite busy or locked), anywhere in the inner or aggregate chain. `IsConcurrencyConflictByTypeName` keeps the type-name match alone.
- `RequireLease = true` makes each pass acquire the named lease first. A pass that cannot acquire it is skipped at Debug and the loop waits `Interval`.
- `LeaseName` defaults to the command type's full name, or the source type's full name for batch loops. Loops sharing a name single-flight against each other.
- Batch loops use the same `RecurringCommandHostOptions` timing values, validation, snapshotting, and `Enabled` behavior as single-command loops.

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
- An empty batch sends nothing and the loop waits `Interval`. A batch that a refusal rolls back waits `ErrorBackoff`. A committed batch that reports more work starts the next pass without waiting.
- Commands returned by a source must be registered with the command module.

## Non-Goals

- Cron expressions, wall-clock alignment, and time zones.
- Missed-run catch-up for time the host was down.
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
| `2107` `BatchRefused` | Warning | A batch was refused and rolled back, so the loop continues after `ErrorBackoff` |
| `2110` `LeaseRenewalStolen` | Warning | The lease was taken by another holder, so renewal stopped and the row was left untouched |
| `2111` `LeaseRenewalFailed` | Warning | A lease renewal failed, so renewal stopped and the lease expires on the database clock |

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

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Loop under a real generic host start and stop | Yes | Covered indirectly through the manifest assertion only | Integration | Low |
| Two instances sending the same recurring command | Yes, by design | No test asserts the concurrent shape | Integration | Low |
