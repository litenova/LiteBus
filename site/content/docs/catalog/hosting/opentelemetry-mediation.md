# Mediation OpenTelemetry Registration

## Header

- **ID**: `hosting.opentelemetry-mediation`
- **Name**: Mediation OpenTelemetry registration
- **Maturity**: GA
- **Summary**: Registers the mediation activity source and meter names on OpenTelemetry builder pipelines.

## What It Does

`LiteBus.Messaging.Extensions.OpenTelemetry` provides registration helpers:

- `AddLiteBusMediationInstrumentation()` for tracing
- `AddLiteBusMediationMetrics()` for metrics

These methods subscribe to public constants from `LiteBusMediationTelemetry`. The instruments record whether or not anything listens; these two calls are what makes them reach an exporter.

One span is started per mediation by default. Per-stage child spans are opt-in through `MessageModuleBuilder.UseTelemetry(MediationTelemetryOptions)`, because mediation volume is orders of magnitude above durable processing volume. Metrics are recorded by default and per-stage durations are opt-in; `MediationTelemetryOptions.Disabled` records nothing.

## Public Surface

### Registration

- `TracerProviderBuilder AddLiteBusMediationInstrumentation(this TracerProviderBuilder builder)`
- `MeterProviderBuilder AddLiteBusMediationMetrics(this MeterProviderBuilder builder)`

### Consumer Contracts

- `LiteBusMediationTelemetry.ActivitySourceName` (`LiteBus.Mediation`)
- `LiteBusMediationTelemetry.MeterName` (`LiteBus.Mediation`)
- `LiteBusMediationTelemetry.DurationInstrumentName` (`litebus.mediation.duration`, histogram in milliseconds)
- `LiteBusMediationTelemetry.CountInstrumentName` (`litebus.mediation.count`, counter per completed mediation)
- `LiteBusMediationTelemetry.StageDurationInstrumentName` (`litebus.mediation.stage.duration`, histogram per pre stage, opt-in)
- `LiteBusMediationTelemetry.DecisionsInstrumentName` (`litebus.mediation.decisions`, counter when a stage stops a mediation)
- Attribute keys: `litebus.message`, `litebus.outcome`, `litebus.code`, `litebus.stage`, `litebus.decided_by`

### Configuration

- `MessageModuleBuilder.UseTelemetry(MediationTelemetryOptions)`: `Spans` (default `true`), `StageSpans` (default `false`), `Metrics` (default `true`), `StageMetrics` (default `false`)

## Packages

- `LiteBus.Messaging.Extensions.OpenTelemetry`

## Requires

- OpenTelemetry SDK builder pipeline in the application host

## Invariants

- Extension methods validate null builders.
- Source, meter, instrument, and attribute names are stable consumer contracts.
- A mediation a decision stopped carries `litebus.code` on the outcome counter; a denied mediation records the code and is not marked as an error, while a faulted mediation marks its span as an error.
- The decision counter names the stage that stopped the mediation and the handler that decided; a mediation nothing stopped records no decision.

## Non-Goals

- Exporter configuration.
- Dashboard assets.

## Observability

Registers the mediation source and meter only. Instrument emission happens in `LiteBus.Messaging`: the duration histogram and outcome counter record every mediation, the decision counter answers which stage stopped a message, and per-stage durations record only when asked for.

## Test Coverage

### Covered Use Cases

#### `MediationTelemetryTests.One_span_is_started_per_mediation_and_carries_the_outcome`

- **Use case**: one span per mediation with the outcome tag
- **Test kind**: Unit
- **Description**: mediates one command with telemetry enabled and a recording listener
- **Behavior**: reads the recorded activity tags
- **Expected outcome**: one span carrying the message and outcome attributes
- **Remarks**: `tests/LiteBus.Mediator.UnitTests/Pipeline/MediationTelemetryTests.cs`

#### `MediationTelemetryTests.The_decision_counter_names_the_stage_that_stopped_the_mediation`

- **Use case**: decision counter tags
- **Test kind**: Unit
- **Description**: mediates a message a guard denies
- **Behavior**: reads the recorded decision measurement tags
- **Expected outcome**: the stage and deciding handler are named
- **Remarks**: `tests/LiteBus.Mediator.UnitTests/Pipeline/MediationTelemetryTests.cs`

#### `MediationTelemetryTests.Per_stage_durations_are_recorded_when_asked_for`

- **Use case**: opt-in stage metrics
- **Test kind**: Unit
- **Description**: composes with `StageMetrics` enabled
- **Behavior**: mediates one message and reads the meter instruments
- **Expected outcome**: the stage duration histogram is present and tagged by stage
- **Remarks**: `tests/LiteBus.Mediator.UnitTests/Pipeline/MediationTelemetryTests.cs`

#### `MediationTelemetryTests.Disabling_telemetry_records_nothing`

- **Use case**: fully disabled telemetry
- **Test kind**: Unit
- **Description**: composes with `MediationTelemetryOptions.Disabled`
- **Behavior**: mediates one message with recording listeners attached
- **Expected outcome**: no spans and no measurements are recorded
- **Remarks**: `tests/LiteBus.Mediator.UnitTests/Pipeline/MediationTelemetryTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| Provider-subscription scope of `AddLiteBusMediationInstrumentation` and `AddLiteBusMediationMetrics` | Medium | No test builds a provider with these extensions; instrument behavior is covered by `MediationTelemetryTests`. |
| End-to-end exporter payload shape for mediation metrics and traces | Low | Exporters are application-owned and not tested in-repo. |

### Out-of-Scope Use Cases

- OTLP endpoint lifecycle and retention policy.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
- [Architecture](../../architecture/README.md)
