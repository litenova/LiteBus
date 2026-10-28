# Transport Circuit Breaker

- **ID**: `transport.circuit-breaker`
- **Name**: Transport circuit breaker
- **Maturity**: GA
- **Summary**: Destination-scoped publisher circuit breakers that reject broker calls while a bounded failure interval lasts.

## What It Does

Transport modules register one `ITransportCircuitBreakerRegistry`. Each publisher resolves a stable circuit breaker by destination. For example, a failed Kafka topic does not block publication to a healthy topic, and a failed AMQP exchange does not stop ingress consumption.

`TransportCircuitBreaker` opens after the configured number of consecutive broker failures. It measures the break duration with `TimeProvider` monotonic timestamps. After that duration, `AcquirePermit` admits one half-open recovery probe and rejects the other callers. The caller supplies that permit to `RecordSuccess` or `RecordFailure` after the operation completes.

Permits carry an opaque circuit generation. A late success from an operation admitted before another caller opened the circuit cannot close the newer circuit generation. A successful recovery probe closes the circuit. A failed recovery probe starts a new break duration.

Ingress does not read or update publisher circuits. Consumer adapters use their SDK recovery behavior and the supervised LiteBus restart loop. AMQP connection creation uses a separate connection breaker because publishers and consumers share one physical connection.

Publishers record documented broker SDK exceptions. The publisher traces caller cancellation and unexpected application exceptions and rethrows them without changing broker resilience state.

## Public Surface

| Type | Member | Role |
| --- | --- | --- |
| `ITransportCircuitBreaker` | `IsOpen` | Open-state snapshot |
| `ITransportCircuitBreaker` | `FailureCount` | Consecutive failure count |
| `ITransportCircuitBreaker` | `AcquirePermit()` | Admit a normal operation or the single half-open probe |
| `ITransportCircuitBreaker` | `RecordFailure(permit)` | Record a failure for the generation that admitted the operation |
| `ITransportCircuitBreaker` | `RecordSuccess(permit)` | Record success without allowing stale completion to reset newer state |
| `ITransportCircuitBreaker` | `ReleasePermit(permit)` | Release a permit without changing broker resilience state |
| `TransportCircuitBreakerPermit` | opaque value | Carries admission generation and recovery-probe identity |
| `ITransportCircuitBreakerRegistry` | `GetPublisherCircuit(destination)` | Stable publisher breaker scoped by destination |
| `ITransportCircuitBreakerRegistry` | `IsAnyOpen`, `FailureCount` | Aggregate publisher state for metrics |
| `TransportCircuitBreaker` | implementation | Default breaker |
| `TransportCircuitBreakerRegistry` | implementation | Concurrent publisher-scope registry |
| `TransportCircuitBreakerOptions` | `FailureThreshold`, `BreakDuration` | Circuit-breaker tuning (defaults 5 failures and 30 seconds) |
| `TransportCircuitBreakerOpenException` | exception type | Open-circuit failure |
| `AmqpCircuitBreakerOptions` | AMQP option | AMQP-specific circuit-breaker options holder (same 5-failure and 30-second defaults) |

## Packages

- `LiteBus.Transport`
- Adapter modules in `LiteBus.Transport.*`

## Requires

- `transport.metrics`
- `transport.single-broker-registration`

## Invariants

- The breaker is disabled when `FailureThreshold == 0`.
- A positive threshold requires a positive `BreakDuration`; invalid values fail during construction.
- An open rejection does not extend the break duration.
- Only one caller enters half-open state after the break duration.
- Outcomes from an older permit generation cannot change current circuit state.
- Publisher state is isolated by exact destination values.
- Ingress does not update publisher failure state.
- Shared metric names remain on `LiteBusTransportTelemetry` constants.

## Non-Goals

- Consumer handler failure budget policy.
- Automatic secondary broker failover.

## Observability

### Public Metrics Contract

| Instrument | Constant | Type | Value semantics |
| --- | --- | --- | --- |
| `litebus.transport.circuit_breaker.open` | `LiteBusTransportTelemetry.CircuitBreakerOpenInstrumentName` | Observable gauge (`int`) | `1` when any publisher circuit is open or half-open; otherwise `0` |
| `litebus.transport.circuit_breaker.failure_count` | `LiteBusTransportTelemetry.CircuitBreakerFailureCountInstrumentName` | Observable gauge (`long`) | Sum of current consecutive failures across publisher circuits |

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
| `AcquirePermit_AfterBreakDuration_ShouldAdmitOneHalfOpenProbe` | `LiteBus.Transport.UnitTests` |
| `ReleasePermit_DuringHalfOpenProbe_ShouldAllowAnotherProbe` | `LiteBus.Transport.UnitTests` |
| `PublishAsync_WithOpenCircuit_ShouldRethrowCircuitException` | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| `RecordFailure_DuringHalfOpenProbe_ShouldReopenCircuit` | `LiteBus.Transport.UnitTests` |
| `RecordFailure_WhileOpen_ShouldNotExtendBreakDuration` | `LiteBus.Transport.UnitTests` |
| `RecordSuccess_FromPreviousGeneration_ShouldNotCloseCircuit` | `LiteBus.Transport.UnitTests` |
| `GetPublisherCircuit_ShouldScopeStateByDestination` | `LiteBus.Transport.UnitTests` |
| `GenericHost_WhenUnrelatedPublisherCircuitIsOpen_ShouldKeepIngressAndHealthyDispatchRunning` | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| `ProcessPendingAsync_WhenCircuitBreakerOpen_ShouldNotPublish` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`, `Dispatch/Outbox/AwsSqs/`) |
| `ProcessPendingAsync_WhenBrokerUnreachable_ShouldMarkFailedWithVisibleAfter` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Kafka/`, `Dispatch/Outbox/AwsSqs/`) |

### Untested

- Counter export assertions for `failure_recorded` and `opened`.
- Gauge scrape assertions by each broker tag value in integration hosts.
- Break duration expiry behavior on live brokers.

### Out-of-Scope

- Automatic broker failover.

## References

- [metrics.md](metrics.md)
- [Architecture.md](../../architecture/README.md)
- [Amqp-Transport.md](../../integrations/amqp.md)
