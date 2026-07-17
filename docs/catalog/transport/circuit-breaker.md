# Transport Circuit Breaker

- **ID**: `transport.circuit-breaker`
- **Name**: Transport circuit breaker
- **Maturity**: GA
- **Summary**: Shared breaker that counts consecutive publish or connect failures and blocks new operations while open.

## What It Does

`TransportCircuitBreaker` implements `ITransportCircuitBreaker` and is registered by transport modules. `RecordFailure` increments failure count and opens the breaker at threshold. `ThrowIfOpen` fails fast with `TransportCircuitBreakerOpenException` until break duration expires.

`TransportPublishFailurePolicy` filters failures that should increment breaker state. Cancellation (`OperationCanceledException`) does not increment failures.

## Public Surface

| Type | Member | Role |
| --- | --- | --- |
| `ITransportCircuitBreaker` | `IsOpen` | Open-state snapshot |
| `ITransportCircuitBreaker` | `FailureCount` | Consecutive failure count |
| `ITransportCircuitBreaker` | `ThrowIfOpen()` | Fail fast when circuit is open |
| `ITransportCircuitBreaker` | `RecordFailure()` | Increment failure state |
| `ITransportCircuitBreaker` | `RecordSuccess()` | Reset state to closed |
| `TransportCircuitBreaker` | implementation | Default breaker |
| `TransportCircuitBreakerOptions` | `FailureThreshold`, `BreakDuration` | Breaker tuning |
| `TransportPublishFailurePolicy` | `ShouldRecordFailure(Exception)` | Failure classification |
| `TransportCircuitBreakerOpenException` | exception type | Open-circuit failure |
| `AmqpCircuitBreakerOptions` | AMQP option | AMQP-specific breaker options holder |

## Packages

- `LiteBus.Transport`
- Adapter modules in `LiteBus.Transport.*`

## Requires

- `transport.metrics`
- `transport.single-broker-registration`

## Invariants

- Breaker is disabled when `FailureThreshold <= 0` or `BreakDuration == TimeSpan.Zero`.
- Open breaker auto-resets after break duration when checked through `ThrowIfOpen`.
- Shared metric names remain on `LiteBusTransportTelemetry` constants.

## Non-Goals

- Per-destination breaker partitioning.
- Consumer handler failure budget policy.
- Automatic secondary broker failover.

## Observability

### Public Metrics Contract

| Instrument | Constant | Type | Value semantics |
| --- | --- | --- | --- |
| `litebus.transport.circuit_breaker.open` | `LiteBusTransportTelemetry.CircuitBreakerOpenInstrumentName` | Observable gauge (`int`) | `1` open, `0` closed |
| `litebus.transport.circuit_breaker.failure_count` | `LiteBusTransportTelemetry.CircuitBreakerFailureCountInstrumentName` | Observable gauge (`long`) | Consecutive failures |

Common meter and tag constants:

| Constant | Value |
| --- | --- |
| `LiteBusTransportTelemetry.MeterName` | `LiteBus.Transport` |
| `LiteBusTransportTelemetry.BrokerTagName` | `litebus.transport.broker` |

Broker tag values set by module registration:

| Module | Tag value |
| --- | --- |
| `AmqpTransportModule` | `amqp` |
| `KafkaTransportModule` | `kafka` |
| `AwsSqsTransportModule` | `sqs` |
| `AzureServiceBusTransportModule` | `azure_service_bus` |
| `InMemoryTransportModule` | `inmemory` |

### Internal Transition Counters

`TransportCircuitBreakerTelemetry` also records internal counters on the same meter:

| Instrument | Type | Trigger |
| --- | --- | --- |
| `litebus.transport.circuit_breaker.failure_recorded` | Counter | each qualifying `RecordFailure` while closed |
| `litebus.transport.circuit_breaker.opened` | Counter | state transition from closed to open |

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `RecordFailure_until_threshold_ShouldOpenCircuitAndExposeFailureCount` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `RecordSuccess_after_failures_ShouldCloseCircuit` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `ProcessPendingAsync_WhenCircuitBreakerOpen_ShouldNotPublish` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`, `Dispatch/Outbox/AwsSqs/`) |
| `ProcessPendingAsync_WhenBrokerUnreachable_ShouldMarkFailedWithVisibleAfter` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`, `Dispatch/Outbox/AwsSqs/`) |

### Untested

- Counter export assertions for `failure_recorded` and `opened`.
- Gauge scrape assertions by each broker tag value in integration hosts.
- Break duration expiry behavior on live brokers.

### Out-of-Scope

- Per-queue breaker instances.
- Automatic broker failover.

## Deep Docs

- [metrics.md](metrics.md)
- [Architecture.md](../../architecture/README.md)
- [Amqp-Transport.md](../../integrations/amqp.md)
