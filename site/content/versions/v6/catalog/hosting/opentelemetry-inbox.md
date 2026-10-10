# Inbox OpenTelemetry Registration

## Header

- **ID**: `hosting.opentelemetry-inbox`
- **Name**: Inbox OpenTelemetry registration
- **Maturity**: GA
- **Summary**: Registers inbox activity source and meter names on OpenTelemetry builder pipelines.

## What It Does

`LiteBus.Inbox.Extensions.OpenTelemetry` provides registration helpers:

- `AddLiteBusInboxInstrumentation()` for tracing
- `AddLiteBusInboxMetrics()` for metrics

These methods subscribe to public constants from `LiteBusInboxTelemetry`.

## Public Surface

### Registration

- `TracerProviderBuilder AddLiteBusInboxInstrumentation(this TracerProviderBuilder builder)`
- `MeterProviderBuilder AddLiteBusInboxMetrics(this MeterProviderBuilder builder)`

### Consumer Contracts

- `LiteBusInboxTelemetry.ActivitySourceName`
- `LiteBusInboxTelemetry.MeterName`

## Packages

- `LiteBus.Inbox.Extensions.OpenTelemetry`

## Requires

- OpenTelemetry SDK builder pipeline in the application host

## Invariants

- Extension methods validate null builders.
- Metric and activity source names are stable consumer contracts.

## Non-Goals

- Exporter configuration.
- Dashboard assets.

## Observability

Registers inbox source/meter only. Actual instrument emission comes from inbox runtime components.

## Test Coverage

### Covered Use Cases

#### `LiteBusInboxOpenTelemetryIntegrationTests.TelemetryConstants_ShouldExposeStableConsumerContractNames`

- **Use case**: stable telemetry contract names
- **Test kind**: Integration
- **Description**: validates public constants on inbox telemetry type
- **Behavior**: reads constant values
- **Expected outcome**: source and meter names match expected stable values
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/OpenTelemetry/LiteBusInboxOpenTelemetryIntegrationTests.cs`

#### `LiteBusInboxOpenTelemetryIntegrationTests.AddLiteBusInboxInstrumentation_ShouldSubscribePublicActivitySourceName`

- **Use case**: tracing registration scope
- **Test kind**: Integration
- **Description**: builds tracer provider with inbox instrumentation extension
- **Behavior**: starts activity from inbox source and unrelated source
- **Expected outcome**: inbox activity is captured, unrelated source activity is not
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/OpenTelemetry/LiteBusInboxOpenTelemetryIntegrationTests.cs`

#### `LiteBusInboxOpenTelemetryIntegrationTests.AddLiteBusInboxMetrics_ShouldSubscribePublicMeterName`

- **Use case**: metrics registration scope
- **Test kind**: Integration
- **Description**: builds meter provider with inbox metrics extension and meter listener
- **Behavior**: records inbox metric from inbox meter
- **Expected outcome**: listener observes inbox meter name
- **Remarks**: `tests/LiteBus.Extensions.IntegrationTests/OpenTelemetry/LiteBusInboxOpenTelemetryIntegrationTests.cs`

### Untested Use Cases

| Gap | Priority | Notes |
| --- | --- | --- |
| End-to-end exporter payload shape for inbox metrics and traces | Low | Exporters are application-owned and not tested in-repo. |

### Out-of-Scope Use Cases

- OTLP endpoint lifecycle and retention policy.

## Deep Docs

- [Hosted services](../../architecture/hosted-services.md)
- [Architecture](../../architecture/README.md)
