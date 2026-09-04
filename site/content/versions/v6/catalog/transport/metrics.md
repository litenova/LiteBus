# Transport Metrics

- **ID**: `transport.metrics`
- **Name**: Transport metrics
- **Maturity**: GA
- **Summary**: Shared OpenTelemetry meter and observable circuit-breaker gauges for all transport adapters.

## What It Does

`TransportMetricsRegistration.RegisterIfNeeded` installs one startup initializer (`TransportObservableMetricsInitializer`) and optional broker identity (`TransportBrokerIdentity`). The initializer creates `TransportObservableMetrics`, which exports aggregate publisher state from `ITransportCircuitBreakerRegistry`.

Applications subscribe with `AddLiteBusTransportMetrics()`. AMQP package keeps compatibility alias `AddLiteBusAmqpMetrics()` and points to the same shared meter.

## Public Surface

| API | Role |
| --- | --- |
| `TransportMetricsRegistration.RegisterIfNeeded(IModuleConfiguration, string? broker)` | One-time registration of metrics hooks |
| `TransportObservableMetricsInitializer` | Startup task that creates gauges |
| `TransportObservableMetrics` | Gauge producer bound to aggregate publisher breaker state |
| `TransportBrokerIdentity` | Broker tag identity value |
| `TransportMetricsRegisteredMarker` | Marker to prevent duplicate registration |
| `AddLiteBusTransportMetrics()` | OpenTelemetry meter registration extension |
| `AddLiteBusAmqpMetrics()` | AMQP alias to shared meter |
| `LiteBusTransportTelemetry` constants | Stable meter, instrument, and tag names |

## Packages

- `LiteBus.Transport`
- `LiteBus.Transport.Extensions.OpenTelemetry`
- `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` (alias)

## Requires

- `transport.circuit-breaker`
- Host startup execution for `IStartupTask`

## Invariants

- Shared meter name is `LiteBus.Transport`.
- Public gauge names remain stable on `LiteBusTransportTelemetry`.
- Registration is idempotent per module configuration.

## Non-Goals

- Per-message publish and consume counters.
- Prebuilt dashboard bundles.
- AMQP-specific circuit breaker meter names; use the shared transport instruments with the `amqp` broker tag.

## Observability

### Shared Meter Contract

| Constant | Value |
| --- | --- |
| `LiteBusTransportTelemetry.MeterName` | `LiteBus.Transport` |
| `LiteBusTransportTelemetry.BrokerTagName` | `litebus.transport.broker` |
| `LiteBusTransportTelemetry.CircuitBreakerOpenInstrumentName` | `litebus.transport.circuit_breaker.open` |
| `LiteBusTransportTelemetry.CircuitBreakerFailureCountInstrumentName` | `litebus.transport.circuit_breaker.failure_count` |

### Observable Gauges

| Instrument | Source | Value |
| --- | --- | --- |
| `litebus.transport.circuit_breaker.open` | `ITransportCircuitBreakerRegistry.IsAnyOpen` | `1` when any publisher circuit is open or half-open; otherwise `0` |
| `litebus.transport.circuit_breaker.failure_count` | `ITransportCircuitBreakerRegistry.FailureCount` | sum of current failures across publisher circuits |

### Broker Tag Values

| Broker | Tag value |
| --- | --- |
| AMQP | `amqp` |
| Kafka | `kafka` |
| AWS SQS | `sqs` |
| Azure Service Bus | `azure_service_bus` |
| In-memory | `inmemory` |

### OpenTelemetry Registration

| Extension | Package | Effect |
| --- | --- | --- |
| `AddLiteBusTransportMetrics()` | `LiteBus.Transport.Extensions.OpenTelemetry` | Adds meter `LiteBus.Transport` |
| `AddLiteBusAmqpMetrics()` | `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` | Adds the same meter |

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `RecordFailure_until_threshold_ShouldOpenCircuitAndExposeFailureCount` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `RecordSuccess_after_failures_ShouldCloseCircuit` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `ObservableGauges_ShouldReportBreakerStateAndStopAfterDisposal` | `LiteBus.Transport.UnitTests` |
| `ProcessPendingAsync_WhenCircuitBreakerOpen_ShouldNotPublish` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`, `Dispatch/Outbox/AwsSqs/`) |

### Untested

- Assertions for startup task registration and execution in host composition tests.
- Assertions for OpenTelemetry meter provider subscription (`AddLiteBusTransportMetrics`, `AddLiteBusAmqpMetrics`).
- Broker-tagged gauge export assertions.

### Out-of-Scope

- Transport-level throughput and latency histograms.
- Bundled exporter configurations.

## Deep Docs

- [circuit-breaker.md](circuit-breaker.md)
- [Architecture.md](../../architecture/README.md)
- [Hosted-services.md](../../architecture/hosted-services.md)
