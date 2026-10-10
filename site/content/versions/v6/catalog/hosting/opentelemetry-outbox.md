# Outbox OpenTelemetry Registration

## Header

- **ID**: `hosting.opentelemetry-outbox`
- **Name**: Outbox OpenTelemetry registration
- **Maturity**: GA
- **Summary**: Registers outbox activity source and meter names on OpenTelemetry builder pipelines.

## What It Does

`LiteBus.Outbox.Extensions.OpenTelemetry` exposes registration helpers:

- `AddLiteBusOutboxInstrumentation()` for tracing
- `AddLiteBusOutboxMetrics()` for metrics

These methods subscribe to public constants from `LiteBusOutboxTelemetry`.

## Public Surface

### Registration

- `TracerProviderBuilder AddLiteBusOutboxInstrumentation(this TracerProviderBuilder builder)`
- `MeterProviderBuilder AddLiteBusOutboxMetrics(this MeterProviderBuilder builder)`

### Consumer Contracts

- `LiteBusOutboxTelemetry.ActivitySourceName`
- `LiteBusOutboxTelemetry.MeterName`

## Packages

- `LiteBus.Outbox.Extensions.OpenTelemetry`

## Requires

- OpenTelemetry SDK builder pipeline in the application host

## Invariants

- Extension methods validate null builders.
- Meter and activity source names are stable contract values.

## Non-Goals

- Exporter registration and configuration.

## Observability

Registers outbox source/meter only. Instrument emission happens in outbox runtime package.

## Test Coverage

### Covered Use Cases

#### `LiteBusOutboxOpenTelemetryIntegrationTests.TelemetryConstants_ShouldExposeStableConsumerContractNames`

- **Use case**: stable outbox telemetry constant names
- **Test kind**: Integration
- **Description**: checks public outbox telemetry constants
- **Behavior**: reads `ActivitySourceName` and `MeterName`
- **Expected outcome**: values match expected constants
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/OpenTelemetry/LiteBusOutboxOpenTelemetryIntegrationTests.cs`

#### `LiteBusOutboxOpenTelemetryIntegrationTests.AddLiteBusOutboxInstrumentation_ShouldSubscribePublicActivitySourceName`

- **Use case**: tracing registration scope for outbox source
- **Test kind**: Integration
- **Description**: builds tracer provider with outbox instrumentation
- **Behavior**: starts activity from outbox and unrelated source
- **Expected outcome**: outbox activity is captured, unrelated is ignored
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/OpenTelemetry/LiteBusOutboxOpenTelemetryIntegrationTests.cs`

#### `LiteBusOutboxOpenTelemetryIntegrationTests.AddLiteBusOutboxMetrics_ShouldSubscribePublicMeterName`

- **Use case**: outbox meter registration scope
- **Test kind**: Integration
- **Description**: builds meter provider with outbox metrics extension
- **Behavior**: emits one counter from outbox meter
- **Expected outcome**: listener observes outbox meter
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/OpenTelemetry/LiteBusOutboxOpenTelemetryIntegrationTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| End-to-end exporter payload format for outbox telemetry | Low | Export pipeline is app-owned. |

### Out-of-Scope Use Cases

- Managed dashboard packaging.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
- [Architecture](../../architecture/README.md)
