# AMQP Transport

- **ID**: `transport.amqp`
- **Name**: AMQP transport
- **Maturity**: GA
- **Summary**: RabbitMQ and LavinMQ adapter that implements `ITransportPublisher` and `IMessageConsumer` with explicit manual acknowledgement semantics.

## What It Does

`AmqpTransportModule` composes `AmqpPublisher`, `AmqpConsumer`, and `AmqpConnectionManager`, built on `RabbitMQ.Client`. To publish, the adapter sends to an exchange (`Destination`) with a routing key (`Route`). To consume, it binds a queue using settings from `TransportConsumerOptions` and converts each delivery into a `TransportMessage`.

Registering the AMQP transport adds a transport connectivity diagnostic check and the shared transport circuit-breaker metrics, tagged with broker value `amqp`.

RabbitMQ's default exchange has an empty name and routes directly to the queue named by the routing key. LiteBus scopes each publisher circuit by named exchange, or by routing key for the default exchange, so an unavailable queue behind the default exchange trips only its own circuit and does not reject publications to other queues.

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
| `AmqpMessageReturnedException` | Mandatory publication returned as unroutable |

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
| `AmqpConnectionOptions` | `CircuitBreaker` | `new()` | AMQP circuit-breaker threshold and duration |
| `AmqpConsumerOptions` | `QueueName` | required | Queue to consume |
| `AmqpConsumerOptions` | `PrefetchCount` | `1` | Unacked delivery window |
| `AmqpConsumerOptions` | `MaxInFlightMessages` | `32` | Concurrent handler limit for typed `IAmqpConsumer` subscriptions, matching the generic consumer path |
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

- AMQP is exclusive per process. Adding a second broker module fails the standard duplicate-service composition validation.
- `TransportMessage` ack delegates map directly to `basic.ack` and `basic.nack`.
- Circuit breaker and metrics register once per module configuration using broker tag `amqp`.
- Publisher circuits use the named exchange as their scope. The empty-name default exchange uses its queue routing key.
- A mandatory publication that the broker returns as unroutable throws `AmqpMessageReturnedException` without recording a broker circuit failure. A non-mandatory publication to an unroutable address still succeeds, and the broker drops the message.
- A cancelled connection attempt releases its circuit breaker permit, so the next attempt past the break duration is admitted as the recovery probe.
- The publisher is a singleton and the consumer is transient. A failed consumer start releases its channel and can be retried.
- On a consumed message, `TransportMessage.Destination` is the queue the message arrived from (`AmqpReceivedMessage.QueueName`), falling back to the exchange when the consumer did not track the queue.
- Each delivery settles at most once. A repeat acknowledgement for the same delivery is ignored, and the failure-path requeue is skipped when the handler already settled the delivery.
- A second `StartAsync` call throws `InvalidOperationException`. The token passed to `StartAsync` cancels startup only. `StopAsync` ends the subscription and cancels in-flight handlers.

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
| `PublishAsync_WithDefaultExchange_ShouldScopeCircuitByRoutingKey` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `PublishAsync_WithNamedExchange_ShouldScopeCircuitByExchange` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `PublishThroughRabbitMq_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `PublishThroughLavinMq_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `TransportPublishRequest_Mandatory_ShouldDefaultToTrue` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `AmqpPublishRequest_Mandatory_ShouldDefaultToTrue` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `QueueName_WhenSet_ShouldRoundTrip` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `QueueName_WhenNotSet_ShouldBeNull` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `Build_ShouldRegisterPublisherAsSingleton` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `Build_ShouldRegisterConsumerAsTransient` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `StartAsync_AfterFailedStart_ShouldSucceedAndStop` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `GetConnectionAsync_AfterCancelledHalfOpenProbe_ShouldAdmitNextAttempt` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `PublishAsync_WithMandatoryAndNoBinding_ShouldThrow` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `PublishAsync_WithMandatoryFalseAndNoBinding_ShouldSucceed` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `StartAsync_WhenAlreadyStarted_ShouldThrowInvalidOperationException` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `StartAsync_WhenAlreadyStarted_ThrowsInvalidOperationException` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `StartAsync_AfterStartupTokenCancelledAndDisposed_ShouldStillDeliver` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `StopAsync_ThenStartAsync_ShouldDeliverOnNewSubscription` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `NativeHandlerThatSettlesThenThrows_ShouldNotNackAgain` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `NeutralHandlerThatSettlesThenThrows_ShouldNotReturnToQueueAgain` | `LiteBus.Transport.UnitTests` (`Amqp/`) |

### Untested

- `AmqpConnectivityDiagnosticCheck` behavior in host manifest tests.
- Exported gauge samples that assert broker tag `amqp`.
- Channel-close timing window where handler finishes while channel is closing.

### Out-of-Scope

- Multi-broker failover.
- Production topology automation beyond queue declaration flags.

## References

- [Amqp-Transport.md](../../integrations/amqp.md)
- [Architecture.md](../../architecture/README.md)
- [circuit-breaker.md](circuit-breaker.md)
