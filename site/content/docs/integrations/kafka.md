# Kafka Transport

`LiteBus.Transport.Kafka` publishes and consumes through Kafka with the `Confluent.Kafka` SDK. Inbox and outbox adapters compose it independently: installing the Kafka transport does not pull AMQP or AWS SDKs.

**Production tier: GA** (transport platform and dispatch). **Kafka ingress is Beta**; see the [Feature Index](../reference/feature-index.md).

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Transport.Kafka` | `ITransportPublisher`, `KafkaConsumer`, connection options |
| `LiteBus.Inbox.Dispatch.Kafka` | Outbound command dispatch from inbox processor |
| `LiteBus.Outbox.Dispatch.Kafka` | Outbound event publish from outbox processor |
| `LiteBus.Inbox.Ingress.Kafka` | Broker intake into `IInbox.AcceptAsync` |

Add inbox or outbox core and a storage package alongside the transport. Durable processing needs a store and a processor host; the transport alone persists nothing. See [Dependency Graph](../architecture/dependency-graph.md).

## Registration

```csharp
var kafkaOptions = new KafkaTransportOptions
{
    BootstrapServers = "localhost:9092",
    ConnectivityCheckTimeout = TimeSpan.FromSeconds(3)
};

builder.AddKafkaTransport(kafkaOptions);
builder.AddMessaging(_ => { });
builder.AddInbox(inbox =>
{
    inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
    inbox.UsePostgreSqlStorage(pg => pg.UseConnectionString(connectionString));
    inbox.UseKafkaDispatch(kafka => kafka.DefaultDestination = "orders.commands");

    inbox.UseKafkaIngress(ingress =>
    {
        ingress.UseOptions(new KafkaInboxIngressOptions
        {
            Destination = "orders.commands",
            RequeueOnFailure = true
        });
    });

    inbox.EnableInboxProcessor();
});
```

Register exactly one dispatcher per inbox or outbox: a second dispatcher registration throws `LiteBusConfigurationException`.

## Options Reference

| Type | Property | Default | Notes |
| --- | --- | --- | --- |
| `KafkaTransportOptions` | `BootstrapServers` | required | Broker list |
| | `ClientId` | `null` | Client identifier passed to producer and consumer clients |
| | `ConsumerGroupId` | `litebus-transport` | Consumer group for ingress |
| | `MessageTimeoutMs` | `null` | Producer local message timeout in milliseconds. `null` means the Confluent producer default applies. Producer-side only. |
| | `MessageSendMaxRetries` | `null` | Producer maximum send retries. `null` means the Confluent producer default applies. Producer-side only. |
| | `SocketTimeoutMs` | `null` | Producer socket timeout in milliseconds. `null` means the Confluent producer default applies. Producer-side only. |
| | `ConnectivityCheckTimeout` | 5 s | Readiness cluster-description request timeout |
| | `SeekFailureBackoffInitial` | 250 ms | Delay before re-read after seek |
| | `SeekFailureBackoffMax` | 30 s | Cap on seek backoff |
| | `SeekFailureBackoffMultiplier` | 2.0 | Per-offset failure multiplier |
| `KafkaInboxIngressOptions` | `Destination` | `""` (required) | Topic the ingress consumer subscribes to |
| | `RequeueOnFailure` | `true` | Seek back on transient accept failure |
| | `Safety.MaxInFlightMessages` | `32` requested | Enforced bound is 1: the Kafka adapter runs one handler at a time |

The producer timeout and retry settings affect dispatch only; they do not change the ingress consume loop. For the defaults behind the `null` values, see the [Confluent.Kafka .NET client documentation](https://docs.confluent.io/kafka-clients/dotnet/current/overview.html).

LiteBus does not expose a Kafka `PrefetchCount` setting. The Confluent.Kafka .NET consume loop returns records one at a time from the client's background fetch queue. Kafka fetch tuning remains in `KafkaTransportOptions` or the underlying client configuration when a supported option is added.

## Ingress

`UseKafkaIngress` consumes the `Destination` topic in `ConsumerGroupId` and accepts each record through `IInbox.AcceptAsync`. The registration sample above shows the full setup.

The offset is committed only after `TransportMessage.AcceptAsync` succeeds. On transient accept failure with `RequeueOnFailure = true`, `ReturnToQueueAsync` seeks the consumer back to the failed offset before the next consume, with the seek backoff schedule from `KafkaTransportOptions` (`SeekFailureBackoffInitial`, `SeekFailureBackoffMax`, `SeekFailureBackoffMultiplier`). Poison failures (unknown contract, oversize body, malformed payload) are never requeued.

`Safety` carries the provider-neutral limits: `MaxMessageBytes` (default 4 MiB) rejects oversize bodies before deserialization, `RequireStableIdentity` (default `true`) requires a stable broker delivery id, `TrustApplicationHeaders` (default `false`) keeps application headers from overriding broker identity and tenant metadata, and `AuthorizeDeliveryAsync` optionally authorizes each delivery before accept. The enforced handler concurrency is 1: raising `Safety.MaxInFlightMessages` alone does not parallelize the Kafka adapter.

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| At-least-once delivery when handlers ack after successful accept/dispatch | Exactly-once side effects |
| Offset committed only after `TransportMessage.AcceptAsync` | In-session retry without seek on failure |
| Seek-on-failure rewinds to failed offset before next consume | Ordering across partitions |

On transient ingress failure, `ReturnToQueueAsync` seeks the consumer to the failed offset. The offset is not committed until accept succeeds. Handlers must be idempotent.

## Operations

| Symptom | Check | Action |
| --- | --- | --- |
| Same message reprocessed repeatedly | Consumer lag, seek backoff logs | Fix root accept/dispatch failure; verify idempotency |
| Consumer stuck on one offset | Repeated seek at same partition/offset | Inspect store availability; increase backoff cap if broker pressure |
| No ingress | Topic name, group id, ACLs | Confirm `Destination` topic exists; reset group if needed |
| `transport.kafka.connectivity` unhealthy | Bootstrap reachability, TLS/SASL, cluster ACLs | Fix broker access; tune `ConnectivityCheckTimeout` only when network latency requires it |

Monitor consumer lag, seek retry rate, and inbox/outbox row counts. See [Production Runbook](../operations/runbook.md).

The root transport registers `transport.kafka.connectivity`. It uses Confluent's asynchronous cluster-description API with an explicit [request timeout](https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.Admin.DescribeClusterOptions.html).

## Tests

| Scenario | Location |
| --- | --- |
| Seek on `ReturnToQueueAsync` | `LiteBus.Transport.UnitTests` |
| Ingress transient failure redelivery | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |
| Ingress and dispatch round-trip | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`, `Dispatch/Inbox/Kafka/`, `Dispatch/Outbox/Kafka/`) |

## Related Docs

* [AMQP Transport](amqp.md)
* [AWS SQS Transport](aws-sqs.md)
* [Azure Service Bus Transport](azure-service-bus.md)
* [Inbox AMQP Ingress](inbox-amqp-ingress.md) (ingress patterns shared across brokers)
* [Reliable Messaging](../reliable-messaging/README.md)
* [Integration Tests](../testing/integration-tests.md)
