# Transport OpenTelemetry Registration

- **ID**: `hosting.opentelemetry-transport`
- **Name**: Transport OpenTelemetry registration
- **Maturity**: GA
- **Summary**: Registers the shared transport activity source and meter with OpenTelemetry providers.

## Registration

`LiteBus.Transport.Extensions.OpenTelemetry` provides separate extensions for tracing and metrics:

```csharp
using LiteBus.Transport.Extensions.OpenTelemetry;

services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddLiteBusTransportInstrumentation())
    .WithMetrics(metrics => metrics.AddLiteBusTransportMetrics());
```

The extensions register LiteBus sources only. Exporters, resources, sampling, and reader intervals remain host configuration.

## Public Surface

| API | Registration |
| --- | --- |
| `TracerProviderBuilder.AddLiteBusTransportInstrumentation()` | Activity source `LiteBus.Transport` |
| `MeterProviderBuilder.AddLiteBusTransportMetrics()` | Meter `LiteBus.Transport` |
| `MeterProviderBuilder.AddLiteBusAmqpMetrics()` | AMQP compatibility alias for the same shared meter |

`LiteBusTransportTelemetry` exposes the source, meter, operation, instrument, and tag names used by these extensions.

## Trace Contract

| Constant | Value |
| --- | --- |
| `ActivitySourceName` | `LiteBus.Transport` |
| `PublishOperationName` | `send` |
| `ConsumeOperationName` | `process` |

Activity names include the destination, for example `send orders` and `process commands.inbox`. See [Transport Tracing](../transport/tracing.md) for the attribute and parenting contract.

## Metric Contract

| Instrument | Type | Tags |
| --- | --- | --- |
| `litebus.transport.circuit_breaker.open` | Observable gauge | `litebus.transport.broker` |
| `litebus.transport.circuit_breaker.failure_count` | Observable gauge | `litebus.transport.broker` |
| `litebus.transport.circuit_breaker.failure_recorded` | Counter | None |
| `litebus.transport.circuit_breaker.opened` | Counter | None |

Built-in modules use broker tag values `amqp`, `kafka`, `sqs`, `azure_service_bus`, and `inmemory`. `AddLiteBusAmqpMetrics()` subscribes the shared meter; it does not create an AMQP-only meter.

## Package Boundaries

- `LiteBus.Transport.Extensions.OpenTelemetry` references the shared transport package and OpenTelemetry.
- `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` is an optional AMQP compatibility package.
- Broker SDK packages do not reference OpenTelemetry registration packages.
- Inbox and outbox meters require their own `AddLiteBusInboxMetrics()` and `AddLiteBusOutboxMetrics()` calls.

## Tests

`LiteBusTransportOpenTelemetryIntegrationTests` verifies that:

- The tracing extension subscribes `LiteBus.Transport` and ignores an unrelated source.
- The metrics extension exposes instruments from the `LiteBus.Transport` meter to a provider.

Circuit breaker measurements and disposal behavior are covered by `TransportObservableMetricsTests`.

## Related Documentation

- [Transport Tracing](../transport/tracing.md)
- [Transport Metrics](../transport/metrics.md)
- [Hosted Services](../../architecture/hosted-services.md)
