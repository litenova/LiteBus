# AMQP Transport

- **ID**: `transport.amqp`
- **Name**: AMQP transport
- **Maturity**: GA
- **Summary**: RabbitMQ and LavinMQ adapter that implements `IMessageTransport` and `IMessageConsumer` with explicit manual acknowledgement semantics.

## What It Does

`AmqpTransportModule` composes `AmqpPublisher`, `AmqpConsumer`, and `AmqpConnectionManager` on top of `RabbitMQ.Client`. Publish uses exchange (`Destination`) plus routing key (`Route`). Consume binds to queue settings from `TransportConsumerOptions` and projects each delivery into `TransportMessage`.

AMQP registration includes a transport connectivity diagnostic check and shared transport circuit-breaker metrics tagged with broker value `amqp`.

## Public Surface

### Registration and Runtime Types

| API | Role |
| --- | --- |
| `AmqpTransportModule` | Registers AMQP transport services |
| `IAmqpConnectionManager`, `AmqpConnectionManager` | Connection and channel lifecycle |
| `AmqpPublisher.PublishAsync` | Publish message to exchange and route |
| `AmqpConsumer.StartAsync` | Start queue consume loop |
| `IAmqpPublisher`, `IAmqpConsumer` | AMQP-specific adapter contracts |
| `AmqpConnectivityDiagnosticCheck` | Host manifest connectivity probe |
| `AmqpTransportConfigurationException` | Invalid AMQP configuration error |

### Options

| Type | Property | Default | Purpose |
| --- | --- | --- | --- |
| `AmqpConnectionOptions` | `Uri` | `null` | Full URI override for host and credential fields |
| `AmqpConnectionOptions` | `HostName` | `localhost` | Broker host |
| `AmqpConnectionOptions` | `Port` | `5672` | Broker port |
| `AmqpConnectionOptions` | `VirtualHost` | `/` | AMQP vhost |
| `AmqpConnectionOptions` | `UserName` | `guest` | Username |
| `AmqpConnectionOptions` | `Password` | `guest` | Password |
| `AmqpConnectionOptions` | `ClientProvidedName` | `null` | Connection name visible in broker UI |
| `AmqpConnectionOptions` | `AutomaticRecoveryEnabled` | `true` | Client recovery toggle |
| `AmqpConnectionOptions` | `NetworkRecoveryInterval` | `5s` | Reconnect interval |
| `AmqpConnectionOptions` | `CircuitBreaker` | `new()` | AMQP breaker threshold and duration |
| `AmqpConsumerOptions` | `QueueName` | required | Queue to consume |
| `AmqpConsumerOptions` | `PrefetchCount` | `1` | Unacked delivery window |
| `AmqpConsumerOptions` | `Exclusive` | `false` | Exclusive consumer |
| `AmqpConsumerOptions` | `ConsumerTag` | `null` | Explicit consumer tag |
| `AmqpConsumerOptions` | `DeclareQueue` | `true` | Declare queue before consume |
| `AmqpConsumerOptions` | `DurableQueue` | `true` | Durable queue declaration |
| `AmqpConsumerOptions` | `QueueArguments` | `null` | Queue declaration arguments |

### Ack Mapping

| Transport call | AMQP action |
| --- | --- |
| `AcceptAsync` | `basic.ack` |
| `DiscardAsync` | `basic.nack` with `requeue=false` |
| `ReturnToQueueAsync` | `basic.nack` with `requeue=true` |

## Packages

- `LiteBus.Transport.Amqp`
- `LiteBus.Transport.Amqp.Extensions.OpenTelemetry`

## Requires

- `transport.publish-consume-contracts`
- `transport.manual-acknowledgement`
- `transport.single-broker-registration`

## Invariants

- AMQP is exclusive per process; second transport module registration throws `TransportAlreadyRegisteredException`.
- `TransportMessage` ack delegates map directly to `basic.ack` and `basic.nack`.
- Circuit breaker and metrics register once per module configuration using broker tag `amqp`.

## Non-Goals

- Running multiple broker adapters in one process.
- Owning production exchange and queue topology outside declared consumer queue settings.
- Inbox acceptance, durable idempotency, or processor retries.

## Observability

### Metrics

| Item | Value |
| --- | --- |
| Meter | `LiteBus.Transport` |
| Broker tag | `litebus.transport.broker="amqp"` |
| Gauges | `litebus.transport.circuit_breaker.open`, `litebus.transport.circuit_breaker.failure_count` |
| OpenTelemetry registration | `AddLiteBusTransportMetrics()` or `AddLiteBusAmqpMetrics()` |

### Tracing

- Activity source: `LiteBus.Transport`
- Send span: `send {destination}` with `messaging.system=rabbitmq`
- Process span: `process {destination}` with `messaging.system=rabbitmq`

### Diagnostics

- Diagnostic check id: `transport.amqp.connectivity`
- Type: `AmqpConnectivityDiagnosticCheck`

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `PublishAsync_ThenConsume_AcknowledgesMessage` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `ConsumeAsync_NackWithRequeue_RedeliversMessage` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `PublishAsync_WithLiteBusHeaders_PreservesHeaderValues` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `PublishAsync_AfterConnectionClosed_RecreatesPublishChannel` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `GetConnectionAsync_AfterConnectionClosed_RecreatesWorkingConnection` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `ShouldNack_when_handlerFails_ShouldReturnTrue` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `RecordFailure_until_threshold_ShouldOpenCircuitAndExposeFailureCount` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `RecordSuccess_after_failures_ShouldCloseCircuit` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `PublishThroughRabbitMq_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `PublishThroughLavinMq_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |

### Untested

- `AmqpConnectivityDiagnosticCheck` behavior in host manifest tests.
- Exported gauge samples that assert broker tag `amqp`.
- Channel-close timing window where handler finishes while channel is closing.

### Out-of-Scope

- Multi-broker failover.
- Production topology automation beyond queue declaration flags.

## Deep Docs

- [Amqp-Transport.md](../../integrations/amqp.md)
- [Architecture.md](../../architecture/README.md)
- [circuit-breaker.md](circuit-breaker.md)
