# AMQP Inbox Ingress

- **ID**: `ingress.amqp`
- **Name**: AMQP inbox ingress
- **Maturity**: GA
- **Summary**: Consumes RabbitMQ or LavinMQ queues and accepts commands into the inbox through shared transport ingress.

## What It Does

`UseAmqpIngress` registers `AmqpInboxIngressModule` as an inbox child. The module maps `AmqpInboxIngressOptions` to `TransportInboxIngressOptions`, bootstraps `AmqpTransportModule` when no consumer exists, registers `TransportInboxIngressHandler` and optional `TransportInboxIngressConsumer`, and registers `AmqpInboxIngressHandler` for AMQP-shaped manual accept.

RabbitMQ message ids map to broker-scoped identity and idempotency by default. Queue declaration, prefetch, durable queue, requeue, trusted headers, and batch accept are configurable on the AMQP options type.

## Public Surface

```csharp
inbox.UseAmqpIngress(ingress =>
{
    ingress.UseOptions(new AmqpInboxIngressOptions
    {
        QueueName = "commands.inbox",
        PrefetchCount = 10,
        Connection = connection,
        RequeueOnFailure = true
    });
});
```

| Builder API | Role |
| --- | --- |
| `InboxModuleBuilder.UseAmqpIngress(Action<AmqpInboxIngressModuleBuilder>)` | Registration extension |
| `AmqpInboxIngressModuleBuilder.UseOptions(AmqpInboxIngressOptions)` | Queue and connection settings |
| `AmqpInboxIngressModuleBuilder.DisableIngressConsumer()` | Handler without subscription loop |
| `AmqpInboxIngressModuleBuilder.HostOptions` | `TransportInboxIngressHostOptions` |
| `AmqpInboxIngressHandler` | Manual or test accept wrapper |
| `AmqpInboxIngressModule` | Child module registered via `RegisterIngress` |

### AMQP Baseline Role

AMQP ingress is the current reference adapter for ingress capability depth:

- Full broker option exposure (`TrustApplicationHeaders`, destination declaration, batch accept).
- Full unit coverage for AMQP handler mapping surfaces.
- Broadest broker integration matrix across happy path, failure paths, and batch acceptance.

## Packages

- `LiteBus.Inbox.Ingress.Amqp`
- `LiteBus.Transport.Amqp` (transitive when bootstrapped)

## Requires

- `ingress.registration`
- `ingress.transport-handler`
- `ingress.transport-consumer`
- `transport.amqp`
- Inbox storage and contracts

## Invariants

- `QueueName` is required; compose fails when empty.
- Publishers must send `litebus-contract-name` and `litebus-contract-version` headers.
- At-least-once intake when ack follows successful accept; not exactly-once handler effects.

## Non-Goals

- Azure Service Bus or Kafka intake (separate packages).
- Automatic queue binding to exchanges (declare settings only; routing is ops or publisher concern).
- Outbox publish from ingress.

## Observability

### Ingress Metrics

| Instrument | When incremented |
| --- | --- |
| `ingress.ack_failed_after_accept` | RabbitMQ/LavinMQ ack fails after inbox accept |

Meter `LiteBus.Inbox`. Register with `AddLiteBusInboxMetrics()`.

### Transport Tracing

`process {destination}` records one activity per delivery through `TransportConsumerHandlerInvoker`. Register tracing with `AddLiteBusTransportInstrumentation()` and metrics with `AddLiteBusAmqpMetrics()` or `AddLiteBusTransportMetrics()`.

### Circuit Breaker (AMQP Transport)

| Instrument | Broker tag | When observed |
| --- | --- | --- |
| `litebus.transport.circuit_breaker.open` | `amqp` | Connection or publish path breaker open |
| `litebus.transport.circuit_breaker.failure_count` | `amqp` | Consecutive connectivity failures |

Legacy `litebus.amqp.circuit_breaker.*` meters removed in v6; use shared transport instruments with broker tag.

### Structured Logs

EventId 3002 (loop restart), 3003 (batch flush failed), 3004 (ack failed after accept) from `TransportInboxIngressLogMessages`.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `UseAmqpIngress_WithTransportModule_ShouldRegisterIngressHandler` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `UseAmqpIngress_WithConnectionOptions_ShouldBootstrapAmqpTransport` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `AcceptAsync_ShouldDeserializeAndWriteToInboxWithMappedHeaders` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `PublishThroughRabbitMq_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `PublishThroughLavinMq_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `EnableBatchAccept_AtPrefetchThreshold_ShouldFlushAllMessages` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `EnableBatchAccept_BeforePrefetchThreshold_ShouldFlushAfterBatchMaxWait` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `UnknownContract_ShouldNackWithoutRequeueAndSkipStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `InvalidJson_ShouldNackWithoutRequeueAndSkipStore` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `StoreFull_ShouldNackWithoutRequeueWhenCapacityExceeded` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `RequeueEnabled_WithTransientStoreFailure_ShouldEventuallyAccept` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `PublishThroughRabbitMq_ShouldStoreInPostgreSqlAndDispatchCommand` | `LiteBus.Storage.IntegrationTests` (`PostgreSql/`) |
| `OutboxToInbox_ShouldPublishProcessAndDispatchCommand` | `LiteBus.Storage.IntegrationTests` (`PostgreSql/`) |
| `DuplicateBrokerDelivery_ShouldExecuteHandlerOnce` | `LiteBus.Storage.IntegrationTests` (`PostgreSql/`) |
| `UnknownContract_ShouldNackWithoutRequeueAndSkipPostgreSqlStore` | `LiteBus.Storage.IntegrationTests` (`PostgreSql/`) |
| `InvalidJson_ShouldNackWithoutRequeueAndSkipPostgreSqlStore` | `LiteBus.Storage.IntegrationTests` (`PostgreSql/`) |
| `InboxIngressExtensions_ShouldRegisterIngressServices` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |

### Untested

- LavinMQ-specific failure modes beyond the single end-to-end happy path.
- Queue declaration with `DeclareDestination` and `DurableDestination` against a live broker.
- AMQP circuit breaker open during ingress consume (`litebus.transport.circuit_breaker.open` with broker tag `amqp`).
- `RequeueOnFailure = false` poison drain on RabbitMQ (covered for InMemory and AWS SQS ingress).

### Out-of-Scope

- Azure Service Bus, Kafka, or SQS intake (separate capability pages).
- Automatic queue binding to exchanges.
- Outbox publish from ingress.

## Deep Docs

- [Inbox AMQP ingress](../../integrations/inbox-amqp-ingress.md)
- [AMQP transport](../../integrations/amqp.md)
- [Cookbook: AMQP inbox ingress](../../getting-started/cookbook.md)
