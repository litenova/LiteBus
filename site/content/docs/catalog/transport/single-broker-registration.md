# Single Broker Registration

- **ID**: `transport.single-broker-registration`
- **Name**: Single broker registration
- **Maturity**: GA
- **Summary**: Root composition and dependency-registry validation that allow one transport adapter per host.

## What It Does

Applications register their shared broker once through `AddAmqpTransport`, `AddKafkaTransport`, `AddAwsSqsTransport`, `AddAzureServiceBusTransport`, or `AddInMemoryTransport`. Inbox dispatch, outbox dispatch, and ingress modules depend on that root module through the validated module graph.

If advanced registry code adds a second broker module, composition fails with `ModuleCompositionException`, which extends `LiteBusConfigurationException` and names both modules. The first transport module records its module type in a shared composition context during `Build()`. The second module is refused against that claim.

This rule keeps publisher and consumer runtime behavior aligned with one broker SDK graph and one circuit-breaker identity in a process.

## Public Surface

| API | Role |
| --- | --- |
| `ILiteBusBuilder.AddAmqpTransport(...)` | Registers the shared AMQP root transport |
| `ILiteBusBuilder.AddKafkaTransport(...)` | Registers the shared Kafka root transport |
| `ILiteBusBuilder.AddAwsSqsTransport(...)` | Registers the shared AWS SQS root transport |
| `ILiteBusBuilder.AddAzureServiceBusTransport(...)` | Registers the shared Azure Service Bus root transport |
| `ILiteBusBuilder.AddInMemoryTransport(...)` | Registers the shared in-memory root transport |
| `ModuleCompositionException` | Refuses a second transport module, naming both modules. Extends `LiteBusConfigurationException` |

## Packages

- `LiteBus.Transport.*` (root registration extensions and adapters)
- `LiteBus.Runtime` (module graph and dependency uniqueness validation)

## Requires

- `runtime.module-configuration`
- `transport.publish-consume-contracts`

## Invariants

- Each process holds exactly one `ITransportPublisher` implementation.
- A duplicate broker module fails composition through the standard duplicate-service validation.
- Metric registration stays single-tagged because only one adapter remains after composition.

## Non-Goals

- Dynamic multi-broker routing in one host.
- Runtime broker hot swap without rebuild.
- Meta registration API that pulls all broker adapters into one package graph.

## Observability

- The guard defines no dedicated metrics.
- As an indirect effect, only one broker tag value appears on shared transport metrics.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Build_SecondTransportModule_ShouldThrow` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `Build_SecondTransportModule_ShouldThrow` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `Build_ShouldRejectDuplicateTransportRegistration` | `LiteBus.Transport.UnitTests` (`AzureServiceBus/`) |
| `Build_WithSecondTransportModule_ShouldThrowNamingBothModules` | `LiteBus.Transport.UnitTests` (`Amqp/`) |
| `Build_SecondTransportModule_ShouldNameBothModules` | `LiteBus.Transport.UnitTests` (`Kafka/`) |
| `RegisterIfNeeded_SecondTransportModule_ThrowsNamingBothModules` | `LiteBus.Transport.UnitTests` (`AwsSqs/`) |
| `Build_AzureThenInMemory_ShouldRefuseSecondTransportNamingBoth` | `LiteBus.Transport.UnitTests` (`AzureServiceBus/`) |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `OutboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `InboxIngressExtensions_ShouldRegisterIngressServices` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |

### Untested

- Duplicate registration path where an externally implemented transport module registers only part of the publisher and consumer service set.

### Out-of-Scope

- Multi-broker fan-out in one process.
- Runtime adapter replacement.

## References

- [Architecture.md](../../architecture/README.md)
- [Dependency Graph](../../architecture/dependency-graph.md)
