# Transport Tracing

- **ID**: `transport.tracing`
- **Name**: Transport tracing
- **Maturity**: GA
- **Summary**: Records broker send and message processing activities through one transport activity source.

## Behavior

Concrete broker publishers start one producer activity around the SDK send call. `TransportConsumerHandlerInvoker` starts one consumer activity around the application delivery handler. Durable inbox and outbox dispatchers do not add another transport activity, so one broker operation produces one transport span.

Span names follow the OpenTelemetry messaging form `{operation} {destination}`. Sending to `orders` records `send orders`; processing from `orders` records `process orders`. If no destination is available, the name is `send` or `process`.

## Public Surface

| API | Role |
| --- | --- |
| `TransportTracing.ActivitySource` | Shared `ActivitySource` instance |
| `TransportTracing.StartPublishActivity(TransportActivityMetadata)` | Starts a producer activity |
| `TransportTracing.StartConsumeActivity(TransportMessage)` | Starts a consumer activity |
| `TransportTracing.RecordException(Activity, Exception)` | Records `error.type` and error status |
| `TransportActivityMetadata` | Broker-neutral send activity input |
| `TransportMessagingSystems` | Well-known messaging system values used by built-in adapters |
| `LiteBusTransportTelemetry.ActivitySourceName` | Source name `LiteBus.Transport` |
| `LiteBusTransportTelemetry.PublishOperationName` | Send operation name |
| `LiteBusTransportTelemetry.ConsumeOperationName` | Process operation name |

## Messaging System Values

| Adapter | `messaging.system` |
| --- | --- |
| RabbitMQ-compatible AMQP | `rabbitmq` |
| Amazon SQS | `aws_sqs` |
| Azure Service Bus | `servicebus` |
| Apache Kafka | `kafka` |
| LiteBus in-memory transport | `litebus_in_memory` |
| Custom transport without an identifier | `litebus` |

Custom consumer adapters should set `TransportMessage.MessagingSystem` to the OpenTelemetry value for the broker. The fallback value identifies LiteBus instrumentation but cannot distinguish the underlying system.

## Activity Attributes

Every send and process activity records these required attributes:

| Attribute | Value |
| --- | --- |
| `messaging.system` | Adapter messaging system |
| `messaging.operation.name` | `send` or `process` |
| `messaging.operation.type` | `send` or `process` |

Optional message metadata uses these attributes:

| Attribute | Source |
| --- | --- |
| `messaging.destination.name` | Destination |
| `messaging.message.id` | Message identifier |
| `messaging.message.conversation_id` | Correlation identifier |
| `litebus.transport.route` | Broker-neutral route |
| `litebus.transport.redelivered` | `true` after a prior delivery attempt |
| `messaging.kafka.message.key` | Route on Kafka only |
| `messaging.rabbitmq.destination.routing_key` | Route on RabbitMQ-compatible AMQP only |

`messaging.kafka.message.key` is never emitted for AMQP, SQS, Azure Service Bus, or the in-memory transport.

## Parenting and Stored Trace Context

Send activities retain `Activity.Current` as their parent. Process activities read `TransportHeaders.TraceContext` when it contains either a W3C `traceparent` string or JSON with `traceparent` and optional `tracestate` properties.

When no ambient activity exists, a valid stored W3C context becomes the process parent. When an ambient activity exists, it remains the parent and the stored remote context is added as an activity link. Invalid or malformed trace context is ignored.

LiteBus copies the durable trace context header supplied by the message envelope. Broker-native trace header injection is outside this capability.

## Error Recording

Publisher failures and consumer handler failures set `ActivityStatusCode.Error` and record the canonical exception type in `error.type`. LiteBus does not copy exception messages into transport span attributes or status descriptions. Circuit breaker transitions can add these activity events while a transport activity is current:

- `litebus.transport.circuit_breaker.failure_recorded`
- `litebus.transport.circuit_breaker.opened`

## Registration

Register the source on the tracer provider:

```csharp
using LiteBus.Transport.Extensions.OpenTelemetry;

services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddLiteBusTransportInstrumentation());
```

The extension subscribes `LiteBusTransportTelemetry.ActivitySourceName`. Exporters and sampling remain host configuration.

## Tests

`TransportTracingTests` covers:

- Kafka producer and RabbitMQ consumer attributes.
- Omission of blank optional attributes.
- Ambient send parenting.
- Serialized W3C remote parent continuation.
- Error status and `error.type` recording.

`LiteBusTransportOpenTelemetryIntegrationTests` verifies activity source and meter provider registration.

## Related Documentation

- [Canonical Headers](canonical-headers.md)
- [Circuit Breaker](circuit-breaker.md)
- [Transport OpenTelemetry Registration](../hosting/opentelemetry-transport.md)
