# Recurring Commands

- **ID**: `mediator.recurring-commands`
- **Summary**: Sends one command on a fixed interval for the life of the host, registered through `CommandModuleBuilder.AddRecurring`.
- **Guide**: [Recurring commands](../../integrations/recurring-commands.md)
- **Maturity**: GA

## What It Does

`AddRecurring` declares a child module of `CommandModule` that registers a `RecurringCommandBackgroundService<TCommand>` through the host manifest. Each pass opens its own dispatch scope, resolves `ICommandMediator`, and sends one command produced by the registered factory. A pass that throws is logged and the loop continues after `ErrorBackoff`, except a pass the `IsConcurrencyConflict` predicate classifies as an optimistic-concurrency conflict, which is logged at Information (EventId 2104) and waits `Interval`; an exception never escapes the loop. A result implementing `IRecurringWorkReport` with `HasMoreWork` set skips the interval so the next pass starts immediately. With `RequireLease`, each pass first acquires the named `IRecurringCommandLease`; a pass that cannot acquire it is skipped at Debug (EventId 2105), and one acquired lease covers the whole busy period. When the held lease is lost mid-pass, the loop ends the busy period, releases the handle, and waits `ErrorBackoff`; the in-flight send is cancelled through a token linked to the loss signal.

## Public Surface

### Consumer Contracts

| Member | Purpose |
| --- | --- |
| `CommandModuleBuilder.AddRecurring<TCommand>(Func<TCommand>, Action<RecurringCommandHostOptions>?)` | Declares a loop with an explicit command factory |
| `CommandModuleBuilder.AddRecurring<TCommand>(Action<RecurringCommandHostOptions>?)` | Declares a loop for a command with a parameterless constructor |
| `IRecurringWorkReport.HasMoreWork` | Optional result contract that skips the interval when work remains |
| `RecurringCommandHostOptions` | `Enabled`, `Interval`, `StartupDelay`, `ErrorBackoff`, `IsConcurrencyConflict` (default `IsConcurrencyConflictByTypeName`; `null` treats conflicts as failures), `RequireLease`, `LeaseName` (default command type full name) |
| `IRecurringCommandLease.TryAcquireAsync` | Named single-flight acquisition returning a releasing handle that signals loss, or `null` when held |
| `IRecurringCommandLeaseHandle.LeaseLost` | Token cancelled when the held lease is taken by another holder or renewals fail |
| `CommandModuleBuilder.UsePostgreSqlRecurringLease` | Registers the renewable PostgreSQL lease-row single-flight from `LiteBus.Commands.Recurring.Storage.PostgreSql` |
| `PostgreSqlRecurringLeaseOptions` | Leases table schema and name, `LeaseDuration` (default 30 seconds), `RenewInterval` (default null, meaning one third of the duration; an explicit value must stay below the duration) |
| `RecurringCommandDeclaration<TCommand>` | The registered factory and options, resolved by the loop |

### Registration

- Composed inside `AddCommands`, so a loop cannot be registered without the axis that mediates it. `CommandModuleBuilder.RegisterChildModule(IModule)` is the seam feature packages compose through; `LiteBus.Commands.Recurring` is the worked example.
- Registers `RecurringCommandBackgroundService<TCommand>` through `IModuleConfiguration.RegisterBackgroundService`.
- One registration per command type; each gets its own loop and options.
- A loop with `RequireLease` registers a composition validation that fails the build with `LiteBusConfigurationException` when no `IRecurringCommandLease` is registered; lease implementations register through `RegisterChildModule` so composition can see them.

### Configuration

- Timing values are validated when the module builds, so a zero or negative interval or error backoff fails at boot. The loop snapshots the validated values when it starts, so post-start option mutation does not change the pacing of a running loop.
- `Enabled = false` logs once and never sends.
- `IsConcurrencyConflict` defaults to `IsConcurrencyConflictByTypeName`, which matches a type name ending in `ConcurrencyException` in the inner or aggregate chain.
- `RequireLease = true` makes each pass acquire the named lease first; a pass that cannot acquire it is skipped at Debug and the loop waits `Interval`.
- `LeaseName` defaults to the command type's full name; loops sharing a name single-flight against each other.

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
- By default the loop runs on every host instance; with `RequireLease` each pass runs on at most one instance and one lease covers the busy period.
- A lost lease ends the busy period: the loop releases the handle, waits `ErrorBackoff`, and cancels the in-flight send through a linked token.

## Non-Goals

- Cron expressions, wall-clock alignment, and time zones.
- Missed-run catch-up for time the host was down.
- Standing leader election across a deployment. Single-flight is per pass through an `IRecurringCommandLease`, not a stable leader; work that must be claimed exactly once can also ride the durable axes, which lease and fence per message.

## Observability

| Event | Level | Meaning |
| --- | --- | --- |
| `2101` `LoopStarted` | Information | The loop started, with its interval |
| `2102` `LoopDisabled` | Information | `Enabled` was false, so the loop will not run |
| `2103` `PassFailed` | Error | A send threw; the loop continues after `ErrorBackoff` |
| `2104` `PassConflicted` | Information | A send lost a concurrency race; the loop continues at `Interval` |
| `2105` `LeaseSkipped` | Debug | The lease was held elsewhere, so the pass was skipped; the loop continues at `Interval` |
| `2106` `LeaseLost` | Warning | The held lease was taken or renewals failed, so the busy period ends and the loop backs off |
| `2110` `LeaseRenewalStolen` | Warning | The lease was taken by another holder, so renewal stopped and the row was left alone |
| `2111` `LeaseRenewalFailed` | Warning | A lease renewal failed, so renewal stopped and the lease expires on the database clock |

## Deep Docs

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
