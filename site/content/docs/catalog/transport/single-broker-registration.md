# Single Broker Registration

- **ID**: `transport.single-broker-registration`
- **Name**: Single broker registration
- **Maturity**: GA
- **Summary**: Root composition and dependency-registry validation that allow one transport adapter in a host.

## What It Does

Applications register their shared broker once through `AddAmqpTransport`, `AddKafkaTransport`, `AddAwsSqsTransport`, `AddAzureServiceBusTransport`, or `AddInMemoryTransport`. Inbox dispatch, outbox dispatch, and ingress modules depend on that root module through the validated module graph.

If advanced registry code adds a second broker module, the dependency registry rejects its duplicate singleton services with `LiteBusConfigurationException`. Transport modules do not scan registrations or store context markers during `Build()`.

This rule keeps publisher and consumer runtime behavior aligned to one broker SDK graph and one breaker identity in a process.

## Public Surface

| API | Role |
| --- | --- |
| `ILiteBusBuilder.AddAmqpTransport(...)` | Registers the shared AMQP root transport |
| `ILiteBusBuilder.AddKafkaTransport(...)` | Registers the shared Kafka root transport |
| `ILiteBusBuilder.AddAwsSqsTransport(...)` | Registers the shared AWS SQS root transport |
| `ILiteBusBuilder.AddAzureServiceBusTransport(...)` | Registers the shared Azure Service Bus root transport |
| `ILiteBusBuilder.AddInMemoryTransport(...)` | Registers the shared in-memory root transport |
| `LiteBusConfigurationException` | Reports duplicate service or module composition |

## Packages

- `LiteBus.Transport.*` (root registration extensions and adapters)
- `LiteBus.Runtime` (module graph and dependency uniqueness validation)

## Requires

- `runtime.module-configuration`
- `transport.publish-consume-contracts`

## Invariants

- Exactly one `ITransportPublisher` implementation per process.
- A duplicate broker module fails composition through the standard duplicate-service validation.
- Metric registration stays single-tagged because only one adapter survives compose.

## Non-Goals

- Dynamic multi-broker routing in one host.
- Runtime broker hot swap without rebuild.
- Meta registration API that pulls all broker adapters into one package graph.

## Observability

- No dedicated metrics for guard execution.
- Indirect effect: only one broker tag value exists on shared transport metrics.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Build_SecondTransportModule_ShouldThrow` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `Build_SecondTransportModule_ShouldThrow` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `Build_ShouldRejectDuplicateTransportRegistration` | `LiteBus.Transport.UnitTests` (`AzureServiceBus/`) |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `OutboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `InboxIngressExtensions_ShouldRegisterIngressServices` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |

### Untested

- Duplicate registration path where an externally implemented transport module registers only part of the publisher and consumer service set.

### Out-of-Scope

- Multi-broker fan-out in one process.
- Runtime adapter replacement.

## Deep Docs

- [Architecture.md](../../architecture/README.md)
- [Dependency-Graph.md](../../architecture/dependency-graph.md)
- [AGENTS.md](../../../AGENTS.md)
