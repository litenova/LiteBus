# AMQP Inbox Ingress

- **ID**: `ingress.amqp`
- **Name**: AMQP inbox ingress
- **Maturity**: GA
- **Summary**: This adapter consumes RabbitMQ or LavinMQ queues and accepts commands into the inbox through shared transport ingress.

## Purpose and Scope

`UseAmqpIngress` registers `AmqpInboxIngressModule` as an inbox child. The module maps `AmqpInboxIngressOptions` to `TransportInboxIngressOptions` and requires a root `AmqpTransportModule`. It registers `TransportInboxIngressHandler` and the optional `TransportInboxIngressConsumer`, plus `AmqpInboxIngressHandler` for AMQP-shaped manual accept.

RabbitMQ message ids map to broker-scoped identity and idempotency by default. The idempotency key is scoped to the consuming queue (`ingress:{queue}:{id}`), so two queues bound to one exchange do not share a key.

Queue declaration, native prefetch, and requeue behavior stay on `AmqpInboxIngressOptions`. Trust, authorization, maximum-in-flight work, and batch acceptance use its shared `Safety` record. `AmqpInboxIngressOptions` defaults are `PrefetchCount` 10, `DeclareQueue` true, `DurableQueue` true, and `RequeueOnFailure` true. The enforced `Safety.MaxInFlightMessages` bound is 1.

## Public Surface

```csharp
bus.AddAmqpTransport(connection);
bus.AddInbox(inbox => inbox.UseAmqpIngress(ingress =>
{
    ingress.UseOptions(new AmqpInboxIngressOptions
    {
        QueueName = "commands.inbox",
        PrefetchCount = 10,
        RequeueOnFailure = true,
        Safety = new TransportInboxIngressSafetyOptions
        {
            MaxInFlightMessages = 1,
            EnableBatchAccept = true,
            BatchSize = 5
        }
    });
}));
```

| Builder API | Role |
| --- | --- |
| `InboxModuleBuilder.UseAmqpIngress(Action<AmqpInboxIngressModuleBuilder>)` | Registration extension |
| `AmqpInboxIngressModuleBuilder.UseOptions(AmqpInboxIngressOptions)` | Queue and ingress behavior settings |
| `AmqpInboxIngressModuleBuilder.DisableIngressConsumer()` | Handler without subscription loop |
| `AmqpInboxIngressModuleBuilder.HostOptions` | `TransportInboxIngressHostOptions` |
| `AmqpInboxIngressHandler` | Manual or test accept wrapper |
| `AmqpInboxIngressModule` | Child module registered via `RegisterIngress` |

### AMQP Baseline Role

AMQP ingress is the current reference adapter, with the deepest ingress capability coverage:

- Native AMQP prefetch and destination declaration, plus the shared ingress `Safety` record.
- Full unit coverage for AMQP handler mapping surfaces.
- Broadest broker integration matrix across happy path, failure paths, and batch acceptance.

## Packages

- `LiteBus.Inbox.Ingress.Amqp`
- `LiteBus.Transport.Amqp` (explicit root transport)

## Requires

- `ingress.registration`
- `ingress.transport-handler`
- `ingress.transport-consumer`
- `transport.amqp`
- Inbox storage and contracts

## Invariants

- `QueueName` is required, and composition fails when it is empty.
- `AddAmqpTransport(...)` is required at the root composition boundary.
- Publishers must send `litebus-contract-name` and `litebus-contract-version` headers.
- Intake is at-least-once when ack follows a successful accept. Handler effects are not exactly-once.

## Non-Goals

- Azure Service Bus or Kafka intake (separate packages).
- Automatic queue binding to exchanges (declare settings only, because routing is an ops or publisher concern).
- Outbox publish from ingress.

## Observability

### Ingress Metrics

| Instrument | When incremented |
| --- | --- |
| `ingress.ack_failed_after_accept` | RabbitMQ/LavinMQ ack fails after inbox accept |

The meter is `LiteBus.Inbox`. Register it with `AddLiteBusInboxMetrics()`.

### Transport Tracing

Each delivery records one `process {destination}` activity through `TransportConsumerHandlerInvoker`. Register tracing with `AddLiteBusTransportInstrumentation()` and metrics with `AddLiteBusAmqpMetrics()` or `AddLiteBusTransportMetrics()`.

### Circuit Breaker (AMQP Transport)

| Instrument | Broker tag | When observed |
| --- | --- | --- |
| `litebus.transport.circuit_breaker.open` | `amqp` | At least one destination-scoped publisher circuit is open or half-open |
| `litebus.transport.circuit_breaker.failure_count` | `amqp` | Current failures summed across publisher circuits |

AMQP circuit breaker metrics use the shared transport instruments with the `amqp` broker tag. The adapter emits no AMQP-specific circuit breaker meter names.

### Structured Logs

The consumer logs EventId 3002 (loop restart), 3003 (batch flush failed), and 3004 (ack failed after accept) from `TransportInboxIngressLogMessages`.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `UseAmqpIngress_WithEnabledIngressConsumer_ShouldRegisterConsumerInManifest` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `UseAmqpIngress_WithDisabledIngressConsumer_ShouldOmitConsumerFromManifest` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `AcceptAsync_ShouldDeserializeAndWriteToInboxWithMappedHeaders` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `AcceptAsync_FromTwoQueuesBoundToOneExchange_ShouldProduceTwoRowsWithQueueScopedKeys` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `AcceptAsync_WithoutQueueName_ShouldFallBackToExchange` | `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`) |
| `PublishThroughRabbitMq_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `PublishThroughLavinMq_ShouldAcceptProcessAndDispatchCommand` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `EnableBatchAccept_AtBatchSize_ShouldFlushAllMessages` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| `EnableBatchAccept_BeforeBatchSize_ShouldFlushAfterBatchMaxWait` | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
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
- Queue declaration with `DeclareQueue` and `DurableQueue` against a live broker.
- AMQP connection breaker recovery during ingress consume.
- `RequeueOnFailure = false` poison drain on RabbitMQ (covered for InMemory and AWS SQS ingress).

### Out-of-Scope

- Azure Service Bus, Kafka, or SQS intake (separate capability pages).
- Automatic queue binding to exchanges.
- Outbox publish from ingress.

## References

- [Inbox AMQP ingress](../../integrations/inbox-amqp-ingress.md)
- [AMQP transport](../../integrations/amqp.md)
- [Cookbook: AMQP inbox ingress](../../getting-started/cookbook.md)
