# Single Broker Registration

- **ID**: `transport.single-broker-registration`
- **Name**: Single broker registration
- **Maturity**: GA
- **Summary**: Compose-time guard that blocks registering a second transport adapter in the same module configuration.

## What It Does

Every transport module calls `TransportModuleRegistration.EnsureTransportNotRegistered(...)` during `Build`. The guard scans dependency registrations for existing `IMessageTransport`. If present, it throws `TransportAlreadyRegisteredException`.

This rule keeps publisher and consumer runtime behavior aligned to one broker SDK graph and one breaker identity in a process.

## Public Surface

| API | Role |
| --- | --- |
| `TransportModuleRegistration.EnsureTransportNotRegistered(IModuleConfiguration, string)` | Duplicate registration guard |
| `TransportAlreadyRegisteredException` | Guard failure exception |
| `TransportAlreadyRegisteredException.ModuleName` | Module that attempted duplicate registration |

## Packages

- `LiteBus.Transport` (guard and exception)
- `LiteBus.Transport.*` modules (call sites)

## Requires

- `runtime.module-configuration`
- `transport.publish-consume-contracts`

## Invariants

- Exactly one `IMessageTransport` implementation per process.
- Error message states duplicate module cannot replace active transport registration.
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

- Explicit assertion of `TransportAlreadyRegisteredException.ModuleName` and message text.
- Duplicate registration path where consumer is present but `IMessageTransport` registration is partial.

### Out-of-Scope

- Multi-broker fan-out in one process.
- Runtime adapter replacement.

## Deep Docs

- [Architecture.md](../../architecture/README.md)
- [Dependency-Graph.md](../../architecture/dependency-graph.md)
- [AGENTS.md](../../../AGENTS.md)
