# Recurring Commands

- **ID**: `mediator.recurring-commands`
- **Summary**: Sends one command on a fixed interval for the life of the host, registered through `CommandModuleBuilder.AddRecurring`.
- **Maturity**: GA

## What It Does

`AddRecurring` declares a child module of `CommandModule` that registers a `RecurringCommandBackgroundService<TCommand>` through the host manifest. Each pass opens its own dispatch scope, resolves `ICommandMediator`, and sends one command produced by the registered factory. A pass that throws is logged and the loop continues after `ErrorBackoff`, except a pass the `IsConcurrencyConflict` predicate classifies as an optimistic-concurrency conflict, which is logged at Information (EventId 2104) and waits `Interval`; an exception never escapes the loop. A result implementing `IRecurringWorkReport` with `HasMoreWork` set skips the interval so the next pass starts immediately.

## Public Surface

### Consumer Contracts

| Member | Purpose |
| --- | --- |
| `CommandModuleBuilder.AddRecurring<TCommand>(Func<TCommand>, Action<RecurringCommandHostOptions>?)` | Declares a loop with an explicit command factory |
| `CommandModuleBuilder.AddRecurring<TCommand>(Action<RecurringCommandHostOptions>?)` | Declares a loop for a command with a parameterless constructor |
| `IRecurringWorkReport.HasMoreWork` | Optional result contract that skips the interval when work remains |
| `RecurringCommandHostOptions` | `Enabled`, `Interval`, `StartupDelay`, `ErrorBackoff`, `IsConcurrencyConflict` (default `IsConcurrencyConflictByTypeName`; `null` treats conflicts as failures) |
| `RecurringCommandDeclaration<TCommand>` | The registered factory and options, resolved by the loop |

### Registration

- Composed inside `AddCommands`, so a loop cannot be registered without the axis that mediates it.
- Registers `RecurringCommandBackgroundService<TCommand>` through `IModuleConfiguration.RegisterBackgroundService`.
- One registration per command type; each gets its own loop and options.

### Configuration

- Timing values are validated when the module builds, so a negative interval fails at boot.
- `Enabled = false` logs once and never sends.
- `IsConcurrencyConflict` defaults to `IsConcurrencyConflictByTypeName`, which matches a type name ending in `ConcurrencyException` in the inner or aggregate chain.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Commands.Recurring` | Feature bridge from the command axis to the runtime background-service manifest |

## Requires

- `mediator.commands` for the pipeline each pass runs through
- A host that runs `IBackgroundService` registrations, which the generic host bridging provides

## Invariants

- The command factory is called once per pass, not once at registration.
- A pass failure never ends the loop.
- Cancellation of the host token ends the loop without waiting out the interval.
- The loop runs on every host instance; there is no leader election.

## Non-Goals

- Cron expressions, wall-clock alignment, and time zones.
- Missed-run catch-up for time the host was down.
- Overlap prevention or single-instance election across a deployment. Work that must be claimed exactly once belongs on the durable axes, which lease and fence per message.

## Observability

| Event | Level | Meaning |
| --- | --- | --- |
| `2101` `LoopStarted` | Information | The loop started, with its interval |
| `2102` `LoopDisabled` | Information | `Enabled` was false, so the loop will not run |
| `2103` `PassFailed` | Error | A send threw; the loop continues after `ErrorBackoff` |
| `2104` `PassConflicted` | Information | A send lost a concurrency race; the loop continues at `Interval` |

## Deep Docs

- [Recurring Commands](../../concepts/recurring-commands.md)

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

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Loop under a real generic host start and stop | Yes | Covered indirectly through the manifest assertion only | Integration | Low |
| Two instances sending the same recurring command | Yes, by design | No test asserts the concurrent shape | Integration | Low |
